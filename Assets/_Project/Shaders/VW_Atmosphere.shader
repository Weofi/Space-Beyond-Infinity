Shader "VOIDWARD/Atmosphere"
{
    // Ray-marched atmosphere shell (stylized single scattering). Drawn as the back faces of a sphere around the
    // planet with ZTest Always: every pixel covered by the shell integrates density from the camera (or shell entry)
    // to the first opaque surface (scene depth), the sea/ground sphere or the shell exit. That gives limb glow from
    // orbit, aerial perspective on terrain and props, and a day sky / sunset / dark night sky from the surface.
    Properties
    {
        _Color ("Scatter Color", Color) = (0.45,0.7,1,1)
        _SunsetColor ("Sunset Color", Color) = (1,0.55,0.4,1)
        _HazeColor ("Haze Color", Color) = (0.6,0.75,0.95,1)
        _PlanetRadius ("Planet Radius", Float) = 1500
        _AtmoRadius ("Atmosphere Radius", Float) = 1800
        _GroundRadius ("Ground Radius (sea/min)", Float) = 1450
        _Density ("Density", Range(0,6)) = 1.6
        _Falloff ("Falloff", Range(0.5,10)) = 4
        _Strength ("Brightness", Range(0,4)) = 1
        _NightOpacity ("Night Opacity", Range(0,1)) = 0.2
        _SkyOpacity ("Sky Opacity", Range(0,6)) = 1.8
        _HazeScale ("Haze Over Geometry", Range(0,1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Atmo"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _SunsetColor;
                float4 _HazeColor;
                float _PlanetRadius;
                float _AtmoRadius;
                float _GroundRadius;
                float _Density;
                float _Falloff;
                float _Strength;
                float _NightOpacity;
                float _SkyOpacity;
                float _HazeScale;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.posWS);
                return OUT;
            }

            // Returns (near, far) distances along the ray; far < 0 = miss.
            float2 RaySphere(float3 ro, float3 rd, float r)
            {
                float b = dot(ro, rd);
                float c = dot(ro, ro) - r * r;
                float h = b * b - c;
                if (h < 0.0) return float2(1e9, -1.0);
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 camPos = _WorldSpaceCameraPos;
                float3 ro = camPos - center;
                float3 rd = normalize(IN.posWS - camPos);

                float2 shell = RaySphere(ro, rd, _AtmoRadius);
                if (shell.y <= 0.0) discard;
                float tStart = max(shell.x, 0.0);
                float tEnd = shell.y;

                // First opaque surface along the ray.
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-6;
            #else
                bool sky = raw >= 1.0 - 1e-6;
            #endif
                if (!sky)
                {
                    float eye = LinearEyeDepth(raw, _ZBufferParams);
                    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                    tEnd = min(tEnd, eye / max(dot(rd, camFwd), 1e-3));
                }
                float2 ground = RaySphere(ro, rd, _GroundRadius);
                if (ground.y > 0.0 && ground.x > 0.0 && ground.x < tEnd) { tEnd = ground.x; sky = false; }
                if (tEnd <= tStart) discard;

                Light sun = GetMainLight();
                float3 L = sun.direction;
                float thickness = max(_AtmoRadius - _PlanetRadius, 1.0);
                // Optical depth scale: a horizontal ray through the lower atmosphere reaches ~_Density e-folds.
                float sigma = _Density / thickness;

                const int STEPS = 12;
                float ds = (tEnd - tStart) / STEPS;
                float od = 0.0;
                float3 colSum = 0;
                float litSum = 0.0;
                float sunAlign = dot(rd, L);
                for (int i = 0; i < STEPS; i++)
                {
                    float t = tStart + (i + 0.5) * ds;
                    float3 p = ro + rd * t;
                    float r = length(p);
                    float h = saturate((r - _PlanetRadius) / thickness);
                    float dens = exp(-h * _Falloff) * (1.0 - smoothstep(0.85, 1.0, h));
                    float d = dens * ds * sigma;
                    float mu = dot(p / r, L);
                    float day = smoothstep(-0.22, 0.3, mu);
                    // Warm tint where light grazes the terminator; haze color near the ground.
                    float dusk = saturate(1.0 - abs(mu - 0.02) * 5.0);
                    float3 col = lerp(_Color.rgb, _SunsetColor.rgb, dusk * 0.85);
                    col = lerp(col, _HazeColor.rgb, saturate(dens * 0.6));
                    colSum += col * day * d;
                    litSum += day * d;
                    od += d;
                }
                // Stylized: the open sky is much denser than the haze over nearby geometry, so the day sky is
                // opaque while terrain a few hundred meters away stays readable.
                float k = sky ? _SkyOpacity : _HazeScale;
                float coverage = 1.0 - exp(-od * k);
                float3 avgCol = od > 1e-5 ? colSum / od : 0;
                float litFrac = od > 1e-5 ? saturate(litSum / od) : 0.0;
                // Day sky from inside (#70): pale haze at the horizon, deeper and more saturated toward the zenith.
                float camR = length(ro);
                if (sky && camR < _AtmoRadius)
                {
                    float elev = dot(rd, ro / camR);
                    float g = smoothstep(-0.04, 0.75, elev);
                    float3 horizon = lerp(avgCol, _HazeColor.rgb, 0.4) * 1.06;
                    float lum = dot(avgCol, float3(0.299, 0.587, 0.114));
                    float3 zenith = lerp(lum.xxx, avgCol, 1.2) * 0.8;
                    avgCol = lerp(horizon, zenith, g);
                }
                // Forward-scatter glow around the sun.
                float mie = pow(saturate(sunAlign), 12.0) * 0.6 + pow(saturate(sunAlign), 3.0) * 0.15;
                float3 color = avgCol * coverage * _Strength * (1.0 + mie) * sun.color;
                float alpha = coverage * lerp(_NightOpacity, 1.0, litFrac);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
