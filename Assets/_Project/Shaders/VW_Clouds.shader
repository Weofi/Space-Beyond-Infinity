Shader "VOIDWARD/Clouds"
{
    // Stylized cloud shell: procedural fbm banks on a sphere, crisp toon edges, sun-lit tops and tinted shade.
    // Visible from orbit and from below (double-sided). Drifts slowly around the planet axis.
    Properties
    {
        _Color ("Lit Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.6,0.68,0.85,1)
        _Coverage ("Coverage", Range(0,1)) = 0.4
        _Scale ("Feature Scale", Float) = 3
        _Stretch ("Latitude Stretch", Range(0,4)) = 1
        _Seed ("Seed", Float) = 0
        _Opacity ("Opacity", Range(0,1)) = 0.92
        _Drift ("Drift Speed", Float) = 0.004
        _PlanetRadius ("Cloud Radius", Float) = 1700
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 dirOS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ShadeColor;
                float _Coverage;
                float _Scale;
                float _Stretch;
                float _Seed;
                float _Opacity;
                float _Drift;
                float _PlanetRadius;
            CBUFFER_END

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    s += Noise(p) * a;
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return s / 0.96875;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.posWS);
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirOS);
                float a = _Time.y * _Drift;
                float ca = cos(a), sa = sin(a);
                float3 q = float3(d.x * ca - d.z * sa, d.y, d.x * sa + d.z * ca);
                float3 p = q * _Scale;
                p.y *= 1.0 + _Stretch;
                // Domain warp gives swirly banks instead of round blobs.
                float3 w = float3(Noise(p * 0.7 + 3.1), Noise(p * 0.7 + 9.7), Noise(p * 0.7 + 21.3)) - 0.5;
                float n = Fbm(p + _Seed + w * 1.6);
                float th = 1.0 - _Coverage;
                float body = smoothstep(th, th + 0.012, n);
                if (body <= 0.001) discard;
                float core = smoothstep(th + 0.04, th + 0.14, n);

                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 upWS = normalize(IN.posWS - center);
                Light sun = GetMainLight();
                float ndl = dot(upWS, sun.direction);
                float lit = smoothstep(-0.08, 0.2, ndl);
                float3 camRel = _WorldSpaceCameraPos - center;
                bool below = dot(camRel, camRel) < _PlanetRadius * _PlanetRadius;
                // Thicker cores read darker from below (cloud undersides), brighter rims from above.
                float shade = below ? lerp(0.95, 0.75, core) : lerp(0.9, 1.0, core);
                float3 col = lerp(_ShadeColor.rgb, _Color.rgb, lit * (below ? 0.6 : 1.0)) * shade * lerp(0.2, 1.0, lit);
                float dist = length(IN.posWS - _WorldSpaceCameraPos);
                float alpha = body * _Opacity * lerp(0.4, 1.0, lit) * saturate((dist - 15.0) / 120.0);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
