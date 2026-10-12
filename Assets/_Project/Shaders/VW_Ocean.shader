Shader "VOIDWARD/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.25,0.85,0.85,1)
        _DeepColor ("Deep Color", Color) = (0.04,0.18,0.45,1)
        _WaterDepth ("Depth to Deep Color (m)", Float) = 14
        [HDR] _EmissionColor ("Emission (lava etc.)", Color) = (0,0,0,1)
        _Gloss ("Gloss", Range(0,1)) = 0.9
        _WaveStrength ("Wave Strength", Range(0,2)) = 0.5
        _WaveScale ("Wave Scale (m)", Float) = 9
        _FoamWidth ("Shore Foam Width (m)", Float) = 0.9
        _Refraction ("Refraction", Range(0,0.1)) = 0.02
        [NoScaleOffset] _DetailNormal ("Wave Normal (linear RGB)", 2D) = "bump" {}
        _Steps ("Toon Steps", Range(1,6)) = 3
        _Softness ("Band Softness", Range(0,1)) = 0.65
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.55
        _TerminatorWrap ("Terminator Wrap", Range(0,0.5)) = 0.05
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
                float _Steps;
                float _Softness;
                float _AmbientBoost;
                float _TerminatorWrap;
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

            float3 WaveNormal(float2 uv, float strength)
            {
                float3 t = SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv).xyz * 2.0 - 1.0;
                return float3(t.xy * strength, 0);
            }

            // Two scrolling triplanar normal layers; returns the object-space perturbation (tangential offset).
            float3 Waves(float3 p, float3 n, float strength)
            {
                float3 w = pow(abs(n), 4.0);
                w /= (w.x + w.y + w.z);
                float tm = _Time.y;
                float3 o = 0;
                float2 s1 = float2(0.021, 0.013) * tm, s2 = float2(-0.017, 0.024) * tm;
                float3 a = WaveNormal(p.zy + s1, strength) + WaveNormal(p.zy * 0.43 + s2, strength);
                float3 b = WaveNormal(p.xz + s1, strength) + WaveNormal(p.xz * 0.43 + s2, strength);
                float3 c = WaveNormal(p.xy + s1, strength) + WaveNormal(p.xy * 0.43 + s2, strength);
                o += float3(0, a.y, a.x) * w.x;
                o += float3(b.x, 0, b.y) * w.y;
                o += float3(c.x, c.y, 0) * w.z;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nGeo = normalize(IN.normalOS);
                float3 nGeoWS = normalize(TransformObjectToWorldNormal(nGeo));
                float dist = length(IN.positionWS - _WorldSpaceCameraPos);
                float waveFade = saturate(1.0 - dist / (_WaveScale * 60.0));
                float3 nOS = normalize(nGeo + Waves(IN.positionOS / _WaveScale, nGeo, _WaveStrength * 0.5) * waveFade);
                float3 n = normalize(TransformObjectToWorldNormal(nOS));
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float ndv = saturate(dot(n, viewDir));

                // Water thickness from the scene depth (terrain below), converted to vertical depth.
                float2 suv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float cosRay = max(0.05, dot(-viewDir, camFwd));
                float thick = max(0.0, sceneEye - IN.eyeDepth) / cosRay * saturate(dot(nGeoWS, viewDir));
                float depth01 = saturate(thick / max(0.01, _WaterDepth));

                // Refracted seabed, offset by the wave normal (fall back if the offset sample is in front of the water).
                float2 ruv = suv + mul((float3x3)UNITY_MATRIX_V, n - nGeoWS).xy * _Refraction * saturate(thick * 0.5);
                float rEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rEye < IN.eyeDepth) ruv = suv;
                float3 seabed = SampleSceneColor(ruv);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float ndl = dot(nGeoWS, mainLight.direction);
                float smoothLit = smoothstep(-_TerminatorWrap, 0.65 - _TerminatorWrap * 0.6, ndl);
                float stepped = floor(smoothLit * _Steps + 0.5) / max(_Steps, 1.0);
                float lit = lerp(stepped, smoothLit, _Softness) * lerp(1.0, mainLight.shadowAttenuation, 0.6);
                float3 light = mainLight.color * lit + SampleSH(n) * _AmbientBoost;

                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, sqrt(depth01));
                float absorb = 1.0 - exp(-4.0 * depth01);
                float3 col = lerp(seabed * lerp(float3(1, 1, 1), _ShallowColor.rgb, 0.7), waterCol * light, saturate(absorb + 0.2));

                // Sky reflection + crisp sun glint.
                float fres = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
                float3 refl = GlossyEnvironmentReflection(reflect(-viewDir, n), 1.0 - _Gloss, 1.0);
                col += refl * fres * _Gloss;
                float3 h = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), lerp(48.0, 260.0, _Gloss));
                col += mainLight.color * smoothstep(0.35, 0.9, spec) * _Gloss * 0.8 * step(0.0, ndl);

                // Pulsing shore foam (close range only: depth-buffer precision makes it smear from orbit).
                float shore = (1.0 - saturate(thick / max(0.01, _FoamWidth))) * saturate(1.0 - dist / 150.0);
                float foam = shore * saturate(0.55 + 0.45 * sin(thick / max(0.01, _FoamWidth) * 9.0 - _Time.y * 2.0));
                col = lerp(col, light * 0.95, foam * 0.75);

                col += _EmissionColor.rgb * (0.8 + 0.6 * saturate(length(nOS - nGeo) * 3.0));
                col = MixFog(col, IN.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
