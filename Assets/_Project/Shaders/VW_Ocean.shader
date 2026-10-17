Shader "VOIDWARD/Ocean"
{
    // Planet sea shell (#70). Water: depth-based absorption over the refracted seabed, sky reflection with fresnel, a
    // sun glint whose roughness grows with distance (crisp sparkle up close, a broad soft highlight from orbit), wrap
    // lighting across the terminator and stylized shore foam. Lava (_Lava = 1): flowing dark crust with glowing cracks,
    // hotter in deep channels, no water optics.
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.25,0.85,0.85,1)
        _DeepColor ("Deep Color", Color) = (0.04,0.18,0.45,1)
        _WaterDepth ("Depth to Deep Color (m)", Float) = 14
        [HDR] _EmissionColor ("Emission (lava glow)", Color) = (0,0,0,1)
        _Gloss ("Gloss", Range(0,1)) = 0.9
        _WaveStrength ("Wave Strength", Range(0,2)) = 0.5
        _WaveScale ("Wave Scale (m)", Float) = 9
        _FoamWidth ("Shore Foam Width (m)", Float) = 0.7
        _Refraction ("Refraction", Range(0,0.1)) = 0.02
        [NoScaleOffset] _DetailNormal ("Wave Normal (linear RGB)", 2D) = "bump" {}
        [NoScaleOffset] _DetailTex ("Noise (R, linear)", 2D) = "gray" {}
        _SkyColor ("Sky Color (zenith reflection)", Color) = (0.4,0.65,1,1)
        _HorizonColor ("Horizon Color (grazing reflection)", Color) = (0.75,0.85,0.95,1)
        _HasSky ("Has Atmosphere", Float) = 1
        _Lava ("Lava", Float) = 0
        _CrustColor ("Lava Crust Color", Color) = (0.09,0.06,0.06,1)
        _ShoreFoam ("Shore Foam (1 = surf, low = calm lakes)", Range(0,1)) = 1
        _SSR ("Screen-Space Reflections", Range(0,1)) = 1
    }
    SubShader
    {
        // Before other transparents (atmosphere shells) so they layer on top.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _WaterDepth;
                float4 _EmissionColor;
                float _Gloss;
                float _WaveStrength;
                float _WaveScale;
                float _FoamWidth;
                float _Refraction;
                float4 _SkyColor;
                float4 _HorizonColor;
                float _HasSky;
                float _Lava;
                float4 _CrustColor;
                float _ShoreFoam;
                float _SSR;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS   : TEXCOORD2;
                float  eyeDepth   : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.eyeDepth = -TransformWorldToView(OUT.positionWS).z;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float3 Tri(float3 n)
            {
                float3 w = pow(abs(n), 4.0);
                return w / (w.x + w.y + w.z);
            }

            float2 WaveNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv).xy * 2.0 - 1.0;
            }

            // Two scrolling triplanar normal layers; returns the object-space perturbation (tangential offset).
            float3 Waves(float3 p, float3 w, float strength, float speed)
            {
                float tm = _Time.y * speed;
                float2 s1 = float2(0.021, 0.013) * tm, s2 = float2(-0.017, 0.024) * tm;
                float2 a = WaveNormal(p.zy + s1) + WaveNormal(p.zy * 0.43 + s2);
                float2 b = WaveNormal(p.xz + s1) + WaveNormal(p.xz * 0.43 + s2);
                float2 c = WaveNormal(p.xy + s1) + WaveNormal(p.xy * 0.43 + s2);
                return (float3(0, a.y, a.x) * w.x + float3(b.x, 0, b.y) * w.y + float3(c.x, c.y, 0) * w.z) * strength;
            }

            float TriNoise(float3 p, float3 w)
            {
                return SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.zy).r * w.x
                     + SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.xz).r * w.y
                     + SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.xy).r * w.z;
            }

            // Screen-space reflection (#115): march the reflected ray in world space with growing steps, project each step
            // and compare with the scene depth; on a hit return the opaque colour. Banks, trees and rocks then show up in
            // the water. Returns rgb + confidence (0 = no hit: use the sky).
            // Explicit LOD 0 samples: no derivatives inside the march loop.
            float DepthLod(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv), 0).r; }
            float3 ColorLod(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb; }

            float4 ScreenReflect(float3 posWS, float3 r, float dist, float waterEye)
            {
                const int Steps = 20;
                float stepLen = max(0.25, dist * 0.015);
                float3 p = posWS + r * stepLen * 0.5;
                [loop]
                for (int i = 0; i < Steps; i++)
                {
                    p += r * stepLen;
                    float4 cs = TransformWorldToHClip(p);
                    if (cs.w <= 0.0) break;
                    float2 uv = ComputeNormalizedDeviceCoordinates(p, GetWorldToHClipMatrix());
                    if (any(uv < 0.0) || any(uv > 1.0)) break;
                    float sceneEye = LinearEyeDepth(DepthLod(uv), _ZBufferParams);
                    float delta = cs.w - sceneEye; // > 0: the ray went behind something
                    // Things in front of this water pixel (pawn, ship, near trees) cannot be in its reflection.
                    if (sceneEye < waterEye * 0.9) { stepLen *= 1.35; continue; }
                    if (delta > 0.0 && delta < stepLen * 1.5 + 0.5)
                    {
                        // Refine between the last two steps.
                        float3 a = p - r * stepLen, b = p;
                        for (int k = 0; k < 5; k++)
                        {
                            float3 m = (a + b) * 0.5;
                            float4 mcs = TransformWorldToHClip(m);
                            float2 muv = ComputeNormalizedDeviceCoordinates(m, GetWorldToHClipMatrix());
                            if (mcs.w > LinearEyeDepth(DepthLod(muv), _ZBufferParams)) b = m; else a = m;
                        }
                        float2 huv = ComputeNormalizedDeviceCoordinates(b, GetWorldToHClipMatrix());
                        float2 edge = saturate(min(huv, 1.0 - huv) * 12.0);
                        float conf = edge.x * edge.y * (1.0 - i / (float)Steps);
                        return float4(ColorLod(huv), conf);
                    }
                    stepLen *= 1.35;
                }
                return 0;
            }

            // GGX distribution, clamped so a sharp glint blooms instead of exploding.
            float GGX(float nh, float rough)
            {
                float a2 = rough * rough * rough * rough;
                float d = nh * nh * (a2 - 1.0) + 1.0;
                return min(a2 / (PI * d * d), 60.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nGeo = normalize(IN.normalOS);
                float3 upWS = normalize(TransformObjectToWorldNormal(nGeo));
                float3 w = Tri(nGeo);
                float dist = length(IN.positionWS - _WorldSpaceCameraPos);
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Water thickness from the scene depth (terrain below), converted to vertical depth.
                float2 suv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float cosRay = max(0.05, dot(-viewDir, camFwd));
                float thick = max(0.0, sceneEye - IN.eyeDepth) / cosRay * saturate(dot(upWS, viewDir) + 0.05);

                Light sun = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 L = sun.direction;
                float mu = dot(upWS, L);
                float day = smoothstep(-0.18, 0.25, mu);
                float shadow = lerp(1.0, sun.shadowAttenuation, 0.6);
                // Soft wrap lighting: no toon bands across the sphere, a gentle fade into the night side.
                float diffuse = saturate((mu + 0.3) / 1.3);
                diffuse *= diffuse;
                float3 ambient = SampleSH(upWS) * 0.45;

                if (_Lava > 0.5)
                {
                    // Flowing crust plates: two noise scales drift against each other; cracks between plates glow.
                    float3 p = IN.positionOS / max(1.0, _WaveScale * 2.2);
                    float t = _Time.y * 0.012;
                    float n1 = TriNoise(p + float3(t, 0, t * 0.7), w);
                    float n2 = TriNoise(p * 2.7 - float3(t * 1.3, t, 0), w);
                    float n = n1 * 0.65 + n2 * 0.35;
                    float heat = saturate(thick / max(0.5, _WaterDepth));       // deep channel centres run hotter
                    float far = saturate(dist / 900.0);                            // sub-pixel cracks average out
                    float crack = 1.0 - smoothstep(0.035, 0.11 + heat * 0.08, abs(n - 0.5));
                    crack = lerp(crack, 0.35 + heat * 0.25, far);
                    float pulse = 0.85 + 0.15 * sin(_Time.y * 1.3 + n1 * 12.0);
                    float3 glow = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(crack * 1.2)) * _EmissionColor.rgb * pulse;
                    float3 crust = _CrustColor.rgb * (sun.color * diffuse * shadow + ambient) * (0.7 + n2 * 0.6);
                    // Cooling rim along the banks.
                    float rim = 1.0 - saturate(thick / max(0.1, _FoamWidth * 1.5));
                    float hot = saturate(crack * (1.0 - rim * 0.6) + heat * 0.15);
                    float3 lava = lerp(crust, glow, hot) + glow * 0.08;
                    lava = MixFog(lava, IN.fogCoord);
                    return half4(lava, 1);
                }

                // Near: two wave octaves. Far: a broad swell keeps texture from orbit; finer detail turns into roughness.
                float nearFade = saturate(1.0 - dist / (_WaveScale * 70.0));
                float farFade = saturate(1.0 - dist / (_WaveScale * 900.0));
                float3 nOS = nGeo + Waves(IN.positionOS / _WaveScale, w, _WaveStrength * 0.45, 1.0) * nearFade
                                  + Waves(IN.positionOS / (_WaveScale * 11.0), w, _WaveStrength * 0.25, 0.35) * farFade;
                float3 n = normalize(TransformObjectToWorldNormal(normalize(nOS)));
                float ndv = saturate(dot(n, viewDir));

                // Refracted seabed, offset by the wave normal (fall back if the offset sample is in front of the water).
                float2 ruv = suv + mul((float3x3)UNITY_MATRIX_V, n - upWS).xy * _Refraction * saturate(thick * 0.5) * nearFade;
                float rEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rEye < IN.eyeDepth) ruv = suv;
                float3 seabed = SampleSceneColor(ruv);

                // Absorption: the seabed fades out with depth into a lit water body (shallow -> deep color).
                float depthN = thick / max(0.01, _WaterDepth);
                float transmit = exp(-depthN * 2.2);
                float3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(1.0 - exp(-depthN * 1.4)));
                float3 lightCol = sun.color * diffuse * shadow + ambient;
                float3 col = lerp(body * lightCol, seabed * lerp(1.0, _ShallowColor.rgb, 0.45), transmit * 0.85);

                // Reflection: the planet's own sky (horizon haze at grazing angles, zenith color looking up).
                float3 r = reflect(-viewDir, n);
                float3 sky;
                if (_HasSky > 0.5)
                {
                    float up = saturate(dot(r, upWS));
                    sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(up * 1.8)) * lerp(0.04, 1.0, day) * sun.color;
                }
                else sky = GlossyEnvironmentReflection(r, 0.2, 1.0);
                float fres = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                // Mirror the scene where the reflected ray hits it (close range only: from orbit it is all sky). Skipped
                // where the reflection hardly shows (looking down into the water).
                if (_SSR > 0.0 && dist < 900.0 && fres > 0.06 && dot(r, upWS) > -0.05)
                {
                    float4 ssr = ScreenReflect(IN.positionWS + upWS * 0.05, r, dist, IN.eyeDepth);
                    sky = lerp(sky, ssr.rgb, ssr.a * _SSR * saturate(1.0 - dist / 900.0));
                }
                col = lerp(col, sky, saturate(fres * (0.45 + 0.55 * _Gloss)));

                // Sun glint: roughness grows with distance (sub-pixel waves), so orbit views get a broad sheen.
                float rough = lerp(0.09, 0.34, saturate(dist / 2500.0)) * lerp(1.4, 0.8, _Gloss) + _WaveStrength * 0.03;
                float3 h = normalize(L + viewDir);
                float spec = GGX(saturate(dot(n, h)), rough) * (0.02 + 0.98 * pow(1.0 - saturate(dot(h, viewDir)), 5.0));
                col += sun.color * spec * saturate(dot(n, L)) * shadow * _Gloss;

                // Stylized shore: a lighter shallow lip, a solid contact line and foam bands rolling toward the beach.
                float shoreFade = saturate(1.0 - dist / 220.0);
                if (shoreFade > 0.0)
                {
                    float fw = max(0.05, _FoamWidth);
                    float noise = TriNoise(IN.positionOS / 7.0 + _Time.y * 0.01, w);
                    float contact = 1.0 - smoothstep(0.2, 0.45, thick / fw + (noise - 0.5) * 0.5);
                    // One thin band rolling in toward the contact line, broken up by noise.
                    float band = frac(thick / (fw * 1.6) - _Time.y * 0.2 + noise * 0.4);
                    float bands = smoothstep(0.84, 0.9, band) * (1.0 - smoothstep(0.94, 1.0, band))
                                * (1.0 - saturate(thick / (fw * 2.6))) * smoothstep(0.35, 0.6, noise + 0.15);
                    float foam = saturate(contact * lerp(0.45, 1.0, _ShoreFoam) + bands * 0.7 * _ShoreFoam) * shoreFade;
                    float lip = (1.0 - saturate(thick / (fw * 4.0))) * shoreFade;
                    col = lerp(col, _ShallowColor.rgb * lightCol * 1.15, lip * 0.25 * _ShoreFoam);
                    col = lerp(col, lightCol * 1.1 + 0.08, foam * 0.85);
                }

                col += _EmissionColor.rgb * (0.7 + 0.3 * transmit);
                col = MixFog(col, IN.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
