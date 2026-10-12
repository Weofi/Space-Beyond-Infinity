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
                float _Seed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionCS.z = OUT.positionCS.w; // pin to far plane
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

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);

                // vertical gradient
                float g = saturate(dir.y * 0.5 + 0.5);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(g, 0.75));

                // nebula clouds (two tinted fbm layers)
                float n1 = fbm(dir * _NebulaScale + _Seed);
                float n2 = fbm(dir * (_NebulaScale * 1.7) - _Seed * 0.7 + 31.7);
                float neb = smoothstep(0.42, 0.85, n1) * _NebulaStrength;
                float neb2 = smoothstep(0.5, 0.9, n2) * _NebulaStrength * 0.7;
                col += _NebulaColorA.rgb * neb * 1.6;
                col += _NebulaColorB.rgb * neb2 * 1.4;

                // stars: cell-hashed points with soft falloff
                float3 cellPos = dir * _StarDensity;
                float3 cell = floor(cellPos);
                float h = hash31(cell);
                if (h > 0.9965)
                {
                    float3 center = cell + 0.5;
                    float d = length(frac(cellPos) - 0.5);
                    float star = saturate(1.0 - d * 2.2);
                    star = star * star;
                    float bright = (h - 0.9965) / 0.0035; // 0..1
                    col += _StarColor.rgb * star * lerp(0.4, 2.2, bright);
                }

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
