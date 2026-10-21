/// Metal shading language source for the orb, compiled once at runtime (no resource bundle needed).
///
/// Every element is an instanced quad whose fragment shader evaluates a signed distance field, so edges are
/// anti-aliased analytically and glows are a Gaussian of the distance to the shape — no blur passes.
enum OrbShaderSource {
    static let source = """
    #include <metal_stdlib>
    using namespace metal;

    // a: center.xy (points, y down), rotation (rad), shape
    // b: size.xy, corner radius, glow radius
    // color / color2: straight-alpha sRGB colors (color2 is the gradient tail)
    // extra: core alpha, glow alpha, p0, p1
    struct Instance {
        float4 a;
        float4 b;
        float4 color;
        float4 color2;
        float4 extra;
    };

    struct Uniforms {
        float4 viewport; // width, height (points), scale (pixels per point), unused
    };

    struct VertexOut {
        float4 position [[position]];
        float2 local;
        uint instance [[flat]];
    };

    constant float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, -1), float2(1, 1), float2(-1, 1) };

    vertex VertexOut orb_vertex(uint vid [[vertex_id]],
                                uint iid [[instance_id]],
                                const device Instance *instances [[buffer(0)]],
                                constant Uniforms &uniforms [[buffer(1)]]) {
        Instance item = instances[iid];
        float shape = item.a.w;
        float glow = item.b.w * 3.2;
        float extent;
        if (shape < 0.5) {
            extent = length(item.b.xy) + glow;
        } else if (shape < 1.5) {
            extent = item.b.x + glow;
        } else if (shape < 2.5) {
            extent = item.b.x + item.b.y + glow;
        } else if (shape < 3.5) {
            extent = item.b.x * 0.5 + item.b.y + glow;
        } else if (shape < 4.5) {
            extent = item.extra.w;
        } else {
            extent = item.b.x + item.b.y + glow;
        }
        extent += 2.0 / uniforms.viewport.z;
        float2 local = corners[vid] * extent;
        float2 point = item.a.xy + local;
        VertexOut out;
        out.position = float4(point.x / uniforms.viewport.x * 2.0 - 1.0, 1.0 - point.y / uniforms.viewport.y * 2.0, 0, 1);
        out.local = local;
        out.instance = iid;
        return out;
    }

    fragment float4 orb_fragment(VertexOut in [[stage_in]],
                                 const device Instance *instances [[buffer(0)]],
                                 constant Uniforms &uniforms [[buffer(1)]]) {
        Instance item = instances[in.instance];
        float2 p = in.local;
        float shape = item.a.w;
        float aa = 1.0 / uniforms.viewport.z;
        float4 color = item.color;
        float d;

        if (shape < 0.5) {
            // Rounded rectangle rotated by a.z.
            float c = cos(-item.a.z);
            float s = sin(-item.a.z);
            float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y);
            float r = item.b.z;
            float2 k = abs(q) - item.b.xy + r;
            d = length(max(k, 0.0)) + min(max(k.x, k.y), 0.0) - r;
        } else if (shape < 1.5) {
            d = length(p) - item.b.x;
        } else if (shape < 2.5) {
            d = abs(length(p) - item.b.x) - item.b.y * 0.5;
        } else if (shape < 3.5) {
            // Capsule from tail to head along a.z; color2 at the tail, color at the head.
            float2 direction = float2(cos(item.a.z), sin(item.a.z));
            float half_length = item.b.x * 0.5;
            float t = clamp(dot(p, direction), -half_length, half_length);
            d = length(p - direction * t) - item.b.y * 0.5;
            color = mix(item.color2, item.color, (t + half_length) / max(item.b.x, 0.0001));
        } else if (shape < 4.5) {
            // Radial aura from extra.z (inner) to extra.w (outer), nothing inside the inner radius.
            float r = length(p);
            float inner = item.extra.z;
            float outer = item.extra.w;
            float t = clamp((r - inner) / max(outer - inner, 0.0001), 0.0, 1.0);
            float falloff = exp(-t * t * 4.2) * (1.0 - t * t);
            float edge = clamp((r - inner) / aa + 0.5, 0.0, 1.0);
            float alpha = color.a * falloff * edge * item.extra.x;
            if (alpha <= 0.0005) {
                discard_fragment();
            }
            return float4(color.rgb * alpha, alpha);
        } else {
            // Tapered arc (comet): radius b.x, width b.y, from extra.z over extra.w radians, head at the end.
            float r = length(p);
            float angle = atan2(p.y, p.x);
            float rel = angle - item.extra.z;
            rel -= 6.28318530718 * floor(rel / 6.28318530718);
            if (rel > item.extra.w) {
                discard_fragment();
            }
            float t = rel / item.extra.w;
            d = abs(r - item.b.x) - item.b.y * 0.5 * (0.3 + 0.7 * t);
            color = mix(item.color2, item.color, t);
            color.a *= pow(t, 1.4);
        }

        float coverage = clamp(0.5 - d / aa, 0.0, 1.0);
        float core = coverage * item.extra.x;
        float glow = 0.0;
        if (item.b.w > 0.0) {
            float x = max(d, 0.0) / item.b.w;
            glow = exp(-x * x) * item.extra.y;
        }
        float alpha = (core + glow * (1.0 - core)) * color.a;
        if (alpha <= 0.0005) {
            discard_fragment();
        }
        return float4(color.rgb * alpha, alpha);
    }

    struct TextInstance {
        float4 rect;   // x, y (top-left), width, height in points
        float4 color;  // straight alpha
        float4 glow;   // rgb, strength
        float4 params; // shear, shear pivot y, glow radius (points), unused
    };

    struct TextOut {
        float4 position [[position]];
        float2 uv;
    };

    vertex TextOut orb_text_vertex(uint vid [[vertex_id]],
                                   constant TextInstance &item [[buffer(0)]],
                                   constant Uniforms &uniforms [[buffer(1)]]) {
        float2 uv = corners[vid] * 0.5 + 0.5;
        float2 point = item.rect.xy + uv * item.rect.zw;
        point.x += item.params.x * (item.params.y - point.y);
        TextOut out;
        out.position = float4(point.x / uniforms.viewport.x * 2.0 - 1.0, 1.0 - point.y / uniforms.viewport.y * 2.0, 0, 1);
        out.uv = uv;
        return out;
    }

    fragment float4 orb_text_fragment(TextOut in [[stage_in]],
                                      constant TextInstance &item [[buffer(0)]],
                                      texture2d<float> glyphs [[texture(0)]]) {
        constexpr sampler linear_sampler(address::clamp_to_zero, filter::linear);
        float coverage = glyphs.sample(linear_sampler, in.uv).a;
        float text_alpha = coverage * item.color.a;
        float glow_alpha = 0.0;
        if (item.glow.a > 0.0) {
            float2 step = item.params.z / item.rect.zw;
            float sum = 0.0;
            float weights = 0.0;
            for (int ring = 1; ring <= 4; ring++) {
                float radius = float(ring) / 4.0;
                float weight = exp(-2.2 * radius * radius);
                for (int k = 0; k < 12; k++) {
                    float angle = (float(k) + 0.37 * float(ring)) * 0.5235987756;
                    sum += glyphs.sample(linear_sampler, in.uv + float2(cos(angle), sin(angle)) * step * radius).a * weight;
                    weights += weight;
                }
            }
            glow_alpha = clamp(sum / weights * 1.5, 0.0, 1.0) * item.glow.a * (1.0 - text_alpha);
        }
        float alpha = text_alpha + glow_alpha;
        if (alpha <= 0.0005) {
            discard_fragment();
        }
        return float4(item.color.rgb * text_alpha + item.glow.rgb * glow_alpha, alpha);
    }
    """
}
