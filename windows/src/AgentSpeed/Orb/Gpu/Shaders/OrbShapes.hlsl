// Every element is an instanced quad whose pixel shader evaluates a signed distance field: edges are anti-aliased
// analytically and glows are a Gaussian of the distance to the shape, never a blur pass.
//
// a: center.xy (points), rotation (rad), shape
// b: size.xy, corner radius, glow radius
// color / color2: straight-alpha sRGB (color2 is the gradient tail)
// extra: core alpha, glow alpha, p0, p1
// extra2: fill mode, q0, q1, q2

Texture2D palette : register(t0);

struct ShapeIn
{
    float4 a : INSTANCE0;
    float4 b : INSTANCE1;
    float4 color : INSTANCE2;
    float4 color2 : INSTANCE3;
    float4 extra : INSTANCE4;
    float4 extra2 : INSTANCE5;
    uint vid : SV_VertexID;
};

struct ShapeOut
{
    float4 position : SV_Position;
    float2 local : TEXCOORD0;
    float2 canvas : TEXCOORD1;
    nointerpolation float4 a : TEXCOORD2;
    nointerpolation float4 b : TEXCOORD3;
    nointerpolation float4 color : TEXCOORD4;
    nointerpolation float4 color2 : TEXCOORD5;
    nointerpolation float4 extra : TEXCOORD6;
    nointerpolation float4 extra2 : TEXCOORD7;
};

float2 orb_rotate(float2 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// iq: uneven capsule along +y from radius r1 at the origin to r2 at (0, h).
float orb_uneven_capsule(float2 p, float r1, float r2, float h)
{
    p.x = abs(p.x);
    float b = (r1 - r2) / max(h, 0.0001);
    float a = sqrt(max(1.0 - b * b, 0.0));
    float k = dot(p, float2(-b, a));
    if (k < 0.0)
    {
        return length(p) - r1;
    }

    if (k > a * h)
    {
        return length(p - float2(0.0, h)) - r2;
    }

    return dot(p, float2(a, b)) - r1;
}

// Bounds (lo.xy, hi.xy relative to the center) of an annular sector, so an arc's quad covers only the arc.
float4 orb_arc_bounds(float radius, float half_width, float start, float sweep)
{
    float outer = radius + half_width;
    if (sweep >= orb_tau - 0.001)
    {
        return float4(-outer, -outer, outer, outer);
    }

    float inner = max(radius - half_width, 0.0);
    float2 a = float2(cos(start), sin(start));
    float2 b = float2(cos(start + sweep), sin(start + sweep));
    float2 lo = min(min(a * outer, a * inner), min(b * outer, b * inner));
    float2 hi = max(max(a * outer, a * inner), max(b * outer, b * inner));
    float base = start - orb_tau * floor(start / orb_tau);
    [unroll]
    for (int k = 0; k < 8; k++)
    {
        float axis = float(k) * 1.5707963268;
        float rel = axis - base;
        if (rel >= 0.0 && rel <= sweep)
        {
            float2 d = float2(cos(axis), sin(axis)) * outer;
            lo = min(lo, d);
            hi = max(hi, d);
        }
    }

    return float4(lo, hi);
}

ShapeOut VsShape(ShapeIn input)
{
    float shape = input.a.w;
    float glow = input.b.w * 3.2;
    float extent;
    if (shape < 0.5)
    {
        extent = length(input.b.xy) + glow;
    }
    else if (shape < 1.5)
    {
        extent = input.b.x + glow;
    }
    else if (shape < 2.5)
    {
        extent = input.b.x + input.b.y + glow;
    }
    else if (shape < 3.5)
    {
        extent = input.b.x * 0.5 + input.b.y + glow;
    }
    else if (shape < 4.5)
    {
        extent = input.extra.w;
    }
    else if (shape < 6.5)
    {
        extent = input.b.x + input.b.y + glow;
    }
    else if (shape < 7.5)
    {
        extent = max(input.b.x, input.b.y) + glow;
    }
    else
    {
        extent = max(input.b.x, input.b.y) + max(input.extra.z, input.b.z) + glow;
    }

    extent += 2.0 / viewport.z;
    float2 corner = corners[input.vid];
    float2 local = corner * extent;
    if (shape > 4.5 && shape < 6.5)
    {
        float pad = input.b.y * 0.5 + glow + 2.0 / viewport.z;
        float4 box = orb_arc_bounds(input.b.x, input.b.y * 0.5, input.extra.z, input.extra.w);
        local = lerp(box.xy - pad, box.zw + pad, corner * 0.5 + 0.5);
    }

    float2 p = input.a.xy + local;
    ShapeOut output;
    output.position = orb_clip_position(p);
    output.local = local;
    output.canvas = p;
    output.a = input.a;
    output.b = input.b;
    output.color = input.color;
    output.color2 = input.color2;
    output.extra = input.extra;
    output.extra2 = input.extra2;
    return output;
}

float4 PsShape(ShapeOut input) : SV_Target
{
    float2 p = input.local;
    float shape = input.a.w;
    float aa = 1.0 / viewport.z;
    float4 color = input.color;
    float fill = input.extra2.x;
    float d = 0.0;

    if (shape < 0.5)
    {
        // Rounded rectangle rotated by a.z.
        float2 q = orb_rotate(p, -input.a.z);
        float r = input.b.z;
        float2 k = abs(q) - input.b.xy + r;
        d = length(max(k, 0.0)) + min(max(k.x, k.y), 0.0) - r;
    }
    else if (shape < 1.5)
    {
        d = length(p) - input.b.x;
    }
    else if (shape < 2.5)
    {
        d = abs(length(p) - input.b.x) - input.b.y * 0.5;
    }
    else if (shape < 3.5)
    {
        // Capsule from tail to head along a.z; color2 at the tail, color at the head.
        float2 direction = float2(cos(input.a.z), sin(input.a.z));
        float half_length = input.b.x * 0.5;
        float t = clamp(dot(p, direction), -half_length, half_length);
        d = length(p - direction * t) - input.b.y * 0.5;
        color = lerp(input.color2, input.color, (t + half_length) / max(input.b.x, 0.0001));
    }
    else if (shape < 4.5)
    {
        // Radial aura from extra.z (inner) to extra.w (outer), nothing inside the inner radius.
        float r = length(p);
        float inner = input.extra.z;
        float outer = input.extra.w;
        float t = saturate((r - inner) / max(outer - inner, 0.0001));
        float falloff = exp(-t * t * 4.2) * (1.0 - t * t);
        float edge = saturate((r - inner) / aa + 0.5);
        float alpha = color.a * falloff * edge * input.extra.x * orb_clip_coverage(input.canvas);
        if (alpha <= 0.0005)
        {
            discard;
        }

        return orb_output(color.rgb * alpha, alpha);
    }
    else if (shape < 5.5)
    {
        // Tapered arc (comet): radius b.x, width b.y, from extra.z over extra.w radians, head at the end.
        float r = length(p);
        float angle = atan2(p.y, p.x);
        float rel = angle - input.extra.z;
        rel -= orb_tau * floor(rel / orb_tau);
        if (rel > input.extra.w)
        {
            discard;
        }

        float t = rel / input.extra.w;
        d = abs(r - input.b.x) - input.b.y * 0.5 * (0.3 + 0.7 * t);
        color = lerp(input.color2, input.color, t);
        color.a *= pow(max(t, 0.0), 1.4);
    }
    else if (shape < 6.5)
    {
        // Band arc: radius b.x, width b.y, from extra.z over extra.w radians (clockwise on screen), round caps when
        // b.z > 0.5. Color runs color2 → color (or the heat palette from q0 to q1) and alpha from q2 to 1.
        float r = length(p);
        float start = input.extra.z;
        float sweep = max(input.extra.w, 0.0001);
        float rel = atan2(p.y, p.x) - start;
        rel -= orb_tau * floor(rel / orb_tau);
        float half_width = input.b.y * 0.5;
        float t;
        if (rel <= sweep)
        {
            t = rel / sweep;
            d = abs(r - input.b.x) - half_width;
        }
        else
        {
            float to_end = rel - sweep;
            float to_start = orb_tau - rel;
            if (input.b.z > 0.5)
            {
                float2 ps = input.b.x * float2(cos(start), sin(start));
                float2 pe = input.b.x * float2(cos(start + sweep), sin(start + sweep));
                float ds = length(p - ps) - half_width;
                float de = length(p - pe) - half_width;
                t = de < ds ? 1.0 : 0.0;
                d = min(ds, de);
            }
            else
            {
                t = to_end < to_start ? 1.0 : 0.0;
                float2 k = float2(abs(r - input.b.x) - half_width, min(to_end, to_start) * r);
                d = length(max(k, 0.0)) + min(max(k.x, k.y), 0.0);
            }
        }

        if (fill > 2.5)
        {
            float n = lerp(input.extra2.y, input.extra2.z, t);
            float u = saturate(n) * (255.0 / 256.0) + 0.5 / 256.0;
            color = float4(palette.SampleLevel(linear_clamp, float2(u, 0.5), 0).rgb, input.color.a);
        }
        else
        {
            color = lerp(input.color2, input.color, t);
        }

        color.a *= lerp(input.extra2.w, 1.0, t);
        fill = 0.0;
    }
    else if (shape < 7.5)
    {
        // Ellipse with radii b.xy rotated by a.z (iq's approximation, exact enough near the edge).
        float2 q = orb_rotate(p, -input.a.z);
        float2 radii = max(input.b.xy, float2(0.001, 0.001));
        float k1 = length(q / radii);
        float k2 = length(q / (radii * radii));
        d = k1 * (k1 - 1.0) / max(k2, 0.00001);
    }
    else
    {
        // Needle blade: pivot at the center, tip at b.x along a.z (half width extra.z → extra.w), counterweight b.y
        // behind (half width extra.z → b.z).
        float2 q = orb_rotate(p, -input.a.z);
        float tip = orb_uneven_capsule(float2(q.y, q.x), input.extra.z, input.extra.w, input.b.x);
        float tail = orb_uneven_capsule(float2(q.y, -q.x), input.extra.z, input.b.z, input.b.y);
        d = min(tip, tail);
    }

    if (fill > 0.5 && fill < 1.5)
    {
        float2 direction = float2(cos(input.extra2.y), sin(input.extra2.y));
        float half_length = max(input.extra2.z, 0.0001);
        float t = saturate((dot(p, direction) + half_length) / (2.0 * half_length));
        color = lerp(input.color, input.color2, t);
    }
    else if (fill > 1.5 && fill < 2.5)
    {
        float t = saturate(length(p - input.extra2.yz) / max(input.extra2.w, 0.0001));
        color = lerp(input.color, input.color2, t);
    }

    float coverage = saturate(0.5 - d / aa);
    float core = coverage * input.extra.x;
    float glow_amount = 0.0;
    if (input.b.w > 0.0)
    {
        float x = max(d, 0.0) / input.b.w;
        glow_amount = exp(-x * x) * input.extra.y;
    }

    float alpha = (core + glow_amount * (1.0 - core)) * color.a * orb_clip_coverage(input.canvas);
    if (alpha <= 0.0005)
    {
        discard;
    }

    return orb_output(color.rgb * alpha, alpha);
}
