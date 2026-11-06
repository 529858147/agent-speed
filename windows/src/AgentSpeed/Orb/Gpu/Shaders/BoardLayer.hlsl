// Cached board layers (DESIGN 2c, Two layers): rows, the hero and the footer, rasterized by Direct2D only when their data
// changes, composited at animated places, faded, tinted (a value's warm flash), swept by a new peak's gold sheen or a top-rung
// meter's shimmer and clipped to the ranking list's frame.

Texture2D layers : register(t2);

struct LayerIn
{
    float4 rect : INSTANCE0;   // x, y (top-left), width, height in points
    float4 uv : INSTANCE1;     // atlas u0, v0, u1, v1
    float4 tint : INSTANCE2;   // rgb (straight), how much of it replaces the layer's color
    float4 params : INSTANCE3; // opacity, sheen center x (points), sheen half width (none when 0), sheen strength (< 0: shimmer)
    float4 clip : INSTANCE4;   // min x, min y, max x, max y; none when max x <= min x
    uint vid : SV_VertexID;
};

struct LayerOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float2 canvas : TEXCOORD1;
    nointerpolation float4 tint : TEXCOORD2;
    nointerpolation float4 params : TEXCOORD3;
    nointerpolation float4 clip : TEXCOORD4;
};

LayerOut VsLayer(LayerIn input)
{
    float2 t = corners[input.vid] * 0.5 + 0.5;
    // Layers land on whole device pixels, so a resting layer's texels map one to one.
    float2 origin = round(input.rect.xy * viewport.z) / viewport.z;
    float2 p = origin + t * input.rect.zw;
    LayerOut output;
    output.position = orb_clip_position(p);
    output.uv = lerp(input.uv.xy, input.uv.zw, t);
    output.canvas = p;
    output.tint = input.tint;
    output.params = input.params;
    output.clip = input.clip;
    return output;
}

float4 PsLayer(LayerOut input) : SV_Target
{
    float keep = 1.0;
    if (input.clip.z > input.clip.x)
    {
        float aa = 1.0 / viewport.z;
        float2 lo = input.clip.xy - input.canvas;
        float2 hi = input.canvas - input.clip.zw;
        keep = saturate(0.5 - max(max(lo.x, lo.y), max(hi.x, hi.y)) / aa);
    }

    float4 texel = layers.SampleLevel(linear_clamp, input.uv, 0);
    if (input.params.w < 0.0)
    {
        // A top-rung meter's shimmer: only the light a 105° white band adds over the layer (overlay with white), summed onto
        // what is drawn, so the meter's lit bars flare and the capsule's faint fill barely changes.
        float2 across = float2(0.9659, 0.2588);
        float2 through = float2(input.params.y, (input.clip.y + input.clip.w) * 0.5);
        float band = -input.params.w * saturate(1.0 - abs(dot(input.canvas - through, across)) / max(input.params.z, 0.0001));
        float3 base = texel.rgb / max(texel.a, 0.0001);
        float3 lit = lerp(float3(1.0, 1.0, 1.0), base * 2.0, step(base, 0.5));
        float weight = band * texel.a * input.params.x * keep * orb_clip_coverage(input.canvas);
        if (weight <= 0.0005)
        {
            discard;
        }

        return orb_output((lit - base) * weight, 0.0);
    }

    float3 rgb = lerp(texel.rgb, input.tint.rgb * texel.a, input.tint.a);
    if (input.params.z > 0.0)
    {
        // The prototypes' gild: white at the band's center, #FFE58A a quarter out, then back to the glyph's own color.
        float d = abs(input.canvas.x - input.params.y) / input.params.z;
        if (d < 1.0)
        {
            float3 gold = float3(1.0, 0.898, 0.541);
            float3 band = d < 0.25 ? lerp(float3(1.0, 1.0, 1.0), gold, d / 0.25) : gold;
            float amount = input.params.w * (d < 0.25 ? 1.0 : 1.0 - (d - 0.25) / 0.75);
            rgb = lerp(rgb, band * texel.a, amount);
        }
    }

    float k = input.params.x * keep * orb_clip_coverage(input.canvas);
    if (texel.a * k <= 0.0005)
    {
        discard;
    }

    return orb_output(rgb * k, texel.a * k);
}
