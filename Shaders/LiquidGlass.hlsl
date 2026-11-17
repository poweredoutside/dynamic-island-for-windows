// HLSL adaptation of the lens model in liquid_glass_easy (MIT, Ahmed Gamil).
// Geometry and Snell refraction are baked by GlassLensMap; the shader only
// samples the backdrop. PS 2.0 also works in WPF's software rendering path.
sampler2D Backdrop : register(s0);
sampler2D LensMap : register(s1);
float4 Sampling : register(c0); // max displacement uv.xy, blur step uv.zw
float4 Tint : register(c1);
float Strength : register(c2);
float2 Pointer : register(c3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float3 lens = tex2D(LensMap, uv).rgb;
    float2 offset = (lens.rg * 255 - 128) / 127 * Sampling.xy * Strength;
    float2 at = clamp(uv + offset, 0.001, 0.999);
    float3 base = tex2D(Backdrop, at).rgb * 0.4;
    base += tex2D(Backdrop, at + float2(Sampling.z, 0)).rgb * 0.15;
    base += tex2D(Backdrop, at - float2(Sampling.z, 0)).rgb * 0.15;
    base += tex2D(Backdrop, at + float2(0, Sampling.w)).rgb * 0.15;
    base += tex2D(Backdrop, at - float2(0, Sampling.w)).rgb * 0.15;
    base = lerp(base, Tint.rgb, Tint.a);
    // A lit rim, rather than a white fill over the entire capsule.
    base = lerp(base, 1, lens.b * 0.7 * Strength);
    float2 glintDelta = (uv - Pointer) * float2(1, 1.8);
    float glint = saturate(1 - dot(glintDelta, glintDelta) * 5);
    base += glint * glint * .045 * Strength;
    return float4(saturate(base), 1);
}
