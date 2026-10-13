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

            float Density(float3 q)
            {
                float3 p = q * _Scale;
                p.y *= 1.0 + _Stretch;
                // Domain warp gives swirly banks instead of round blobs.
                float3 w = float3(Noise(p * 0.7 + 3.1), Noise(p * 0.7 + 9.7), Noise(p * 0.7 + 21.3)) - 0.5;
                return Fbm(p + _Seed + w * 1.6);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dirOS);
                float a = _Time.y * _Drift;
                float ca = cos(a), sa = sin(a);
                float3x3 rot = float3x3(ca, 0, -sa, 0, 1, 0, sa, 0, ca);
                float3 q = mul(rot, d);
                float n = Density(q);
                float th = 1.0 - _Coverage;
                // Soft, fluffy edges (#70): a wide ramp instead of a hard cut-out.
                float body = smoothstep(th - 0.015, th + 0.09, n);
                if (body <= 0.002) discard;
                float core = smoothstep(th + 0.03, th + 0.2, n);

                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 upWS = normalize(IN.posWS - center);
                Light sun = GetMainLight();
                float3 L = sun.direction;
                float ndl = dot(upWS, L);
                float lit = smoothstep(-0.12, 0.25, ndl);

                // Pseudo volume: compare with the density a bit toward the sun. Denser toward the sun = self shadowed.
                float3 tSun = L - upWS * ndl;
                float3 qs = normalize(q + mul(rot, TransformWorldToObjectDir(tSun, false)) * (0.06 / max(_Scale, 0.5)));
                float toward = Density(qs);
                float selfShadow = saturate((toward - n) * 7.0 + 0.5);
                float3 camRel = _WorldSpaceCameraPos - center;
                bool below = dot(camRel, camRel) < _PlanetRadius * _PlanetRadius;
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.posWS);
                float shade = below ? lerp(0.95, 0.62, core) : lerp(1.0, 0.72, selfShadow * core);
                float3 col = lerp(_ShadeColor.rgb, _Color.rgb, lit * (below ? 0.65 : 1.0) * (1.0 - selfShadow * 0.45)) * shade;
                // Silver lining: thin edges glow when looking toward the sun.
                float edge = 1.0 - core;
                col += sun.color * pow(saturate(dot(-viewDir, L)), 6.0) * edge * 0.6 * lit;
                // Warm terminator tint.
                float dusk = saturate(1.0 - abs(ndl - 0.05) * 6.0);
                col *= lerp(float3(1, 1, 1), float3(1.15, 0.85, 0.7), dusk * 0.6);
                col *= lerp(0.12, 1.0, lit) * sun.color;
                float dist = length(IN.posWS - _WorldSpaceCameraPos);
                // Fade out close to the camera: flying through the layer shows mist, not a flat sheet.
                float nearFade = saturate((dist - 25.0) / 350.0);
                float alpha = body * lerp(0.75, 1.0, core) * _Opacity * lerp(0.35, 1.0, lit) * nearFade;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
