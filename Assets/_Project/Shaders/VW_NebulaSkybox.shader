Shader "VOIDWARD/NebulaSkybox"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.05,0.07,0.18,1)
        _HorizonColor ("Horizon Color", Color) = (0.25,0.16,0.38,1)
        _NebulaColorA ("Nebula Color A", Color) = (0.9,0.4,0.7,1)
        _NebulaColorB ("Nebula Color B", Color) = (0.3,0.5,0.95,1)
        _NebulaScale ("Nebula Scale", Range(0.5,8)) = 2.5
        _NebulaStrength ("Nebula Strength", Range(0,3)) = 1.2
        _StarColor ("Star Color", Color) = (1,0.95,0.9,1)
        _StarDensity ("Star Density", Range(10,400)) = 160
        _GalaxyStrength ("Galaxy Strength", Range(0,3)) = 1
        _SunDiscStrength ("Sun Disc Strength", Range(0,4)) = 1
        _Seed ("Variation Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        LOD 100

        Pass
        {
            Name "NebulaSkybox"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _NebulaColorA;
                float4 _NebulaColorB;
                float _NebulaScale;
                float _NebulaStrength;
                float4 _StarColor;
                float _StarDensity;
                float _GalaxyStrength;
                float _SunDiscStrength;
                float _Seed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            #if UNITY_REVERSED_Z
                OUT.positionCS.z = OUT.positionCS.w * 0.000001; // far plane is 0 with reversed Z
            #else
                OUT.positionCS.z = OUT.positionCS.w * 0.999999;
            #endif
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1 + _Seed * 0.013);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i);
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            float fbm(float3 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += vnoise(p) * amp;
                    p *= 2.13;
                    amp *= 0.5;
                }
                return sum;
            }

            float starLayer(float3 dir, float density, float threshold, float size)
            {
                float3 cellPos = dir * density;
                float3 cell = floor(cellPos);
                float h = hash31(cell);
                if (h <= threshold) return 0.0;
                float3 jitter = float3(hash31(cell + 11.3), hash31(cell + 23.9), hash31(cell + 37.1)) * 0.3 + 0.35;
                float d = length(frac(cellPos) - jitter);
                float star = saturate(1.0 - d * size);
                float bright = (h - threshold) / (1.0 - threshold);
                return star * star * star * lerp(0.25, 1.0, bright * bright);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);

                // Near-black base with a barely visible gradient (zenith/horizon come from the sector palette).
                float g = saturate(dir.y * 0.5 + 0.5);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(g, 0.75));

                // Faint nebula wisps, masked by a large-scale band so most of the sky stays black.
                float band = smoothstep(0.45, 0.75, fbm(dir * 0.9 + _Seed * 0.3 + 5.3));
                float n1 = fbm(dir * _NebulaScale + _Seed);
                float n2 = fbm(dir * (_NebulaScale * 1.7) - _Seed * 0.7 + 31.7);
                float neb = smoothstep(0.45, 0.9, n1) * band * _NebulaStrength;
                float neb2 = smoothstep(0.52, 0.92, n2) * band * _NebulaStrength * 0.7;
                col += _NebulaColorA.rgb * neb * 0.35;
                col += _NebulaColorB.rgb * neb2 * 0.3;

                // Distant galaxies: rare soft elongated smudges with a bright core.
                float3 gp = dir * 4.0;
                float3 gc = floor(gp);
                if (hash31(gc + 71.7) > 0.86)
                {
                    float3 f = frac(gp) - 0.5;
                    float3 ax = normalize(float3(hash31(gc + 1.3), hash31(gc + 2.7), hash31(gc + 5.1)) - 0.5);
                    float along = dot(f, ax);
                    float3 perp = f - ax * along;
                    float d2 = along * along + dot(perp, perp) * 7.0;
                    float tint = hash31(gc + 9.9);
                    float3 gcol = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.85, 0.7), tint);
                    col += gcol * (exp(-d2 * 260.0) * 0.18 + exp(-d2 * 4000.0) * 0.6) * _GalaxyStrength;
                }

                // Stars: dense faint dust + sparse bright stars, slightly tinted.
                float dust = starLayer(dir, _StarDensity * 2.2, 0.978, 4.0) * 0.55;
                float bright = starLayer(dir, _StarDensity, 0.9965, 3.0) * 2.4;
                float starTint = hash31(floor(dir * _StarDensity));
                float3 sc = _StarColor.rgb * lerp(float3(0.8, 0.88, 1.0), float3(1.0, 0.92, 0.8), starTint);
                col += sc * (dust + bright) * (1.0 - neb * 0.5);

                // Sun disc + soft corona in the main light direction (bloom does the rest).
                float3 sunCol = _MainLightColor.rgb / max(max(_MainLightColor.r, _MainLightColor.g), max(_MainLightColor.b, 0.001));
                float sd = saturate(dot(dir, _MainLightPosition.xyz));
                col += sunCol * (smoothstep(0.99985, 0.99993, sd) * 30.0 + pow(sd, 900.0) * 1.5 + pow(sd, 40.0) * 0.06) * _SunDiscStrength;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
