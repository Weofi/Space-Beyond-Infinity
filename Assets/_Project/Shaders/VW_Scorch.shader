Shader "VOIDWARD/Scorch"
{
    // Procedural burn mark / impact decal (#43) on a small quad laid onto the hit surface.
    // One pass, premultiplied blend: alpha darkens what is underneath (soot), rgb adds the cooling glow of
    // the hot core. Per-decal values come from a MaterialPropertyBlock (DecalPool), no textures needed.
    Properties
    {
        [HDR] _HeatColor ("Heat Color", Color) = (3, 1.2, 0.35, 1)
        _Heat ("Heat 0..1", Range(0,1)) = 1
        _Fade ("Fade 0..1", Range(0,1)) = 1
        _Darkness ("Darkness", Range(0,1)) = 0.85
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Scorch"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                float4 _HeatColor;
                float _Heat;
                float _Fade;
                float _Darkness;
                float _Seed;
            CBUFFER_END
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.fog = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            half4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv - 0.5;
                float d = length(c) * 2.0;                       // 0 centre .. 1 quad edge
                float ang = atan2(c.y, c.x);
                float2 sp = c * 7.0 + _Seed * 13.7;
                float n = vnoise(sp) * 0.65 + vnoise(sp * 2.3 + 4.1) * 0.35;
                // ragged outline + soot splash rays
                float lump = 0.07 * sin(ang * 3.0 + _Seed * 6.3) + 0.04 * sin(ang * 7.0 - _Seed * 2.1) + (n - 0.5) * 0.35;
                // irregular soot blot: blotchy noise edge, darker charred ring around the (melted) centre
                float soot = 1.0 - smoothstep(0.3, 0.8, d - lump);
                float core = 1.0 - smoothstep(0.0, 0.42, d + (n - 0.5) * 0.25);
                float ring = smoothstep(0.08, 0.3, d) * (1.0 - smoothstep(0.3, 0.6, d - lump * 0.5));
                float a = saturate(soot * (0.6 + 0.25 * core + 0.3 * ring) * (0.7 + 0.3 * n)) * _Darkness * _Fade;
                // hot core: bright centre with soft ember speckles; cools from white-hot through the heat colour to nothing
                float embers = smoothstep(0.55, 0.9, vnoise(sp * 4.0 - _Seed) * vnoise(sp * 1.7 + _Seed)) * (1.0 - smoothstep(0.1, 0.6, d));
                float hot = (core * core * 1.2 + embers * 0.5) * _Heat * _Heat;
                float3 glow = _HeatColor.rgb * hot + float3(1.0, 0.9, 0.7) * pow(core, 4.0) * _Heat * _Heat * _Heat * 2.0;
                // fog: the glow fades into fog, the soot loses its darkening with distance
                float vis = MixFogColor(float3(1, 1, 1), float3(0, 0, 0), IN.fog).r;
                return half4(glow * _Fade * vis, a * vis);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
