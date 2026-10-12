Shader "VOIDWARD/Planet"
{
    Properties
    {
        [NoScaleOffset] _Ramp ("Color Ramp (row0 ground, row1 cliff)", 2D) = "white" {}
        [NoScaleOffset] _DetailTex ("Detail (R, linear)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Detail Normal (linear RGB)", 2D) = "bump" {}
        _DetailScale ("Detail Scale (m)", Float) = 6
        _DetailStrength ("Detail Albedo Strength", Range(0,1)) = 0.35
        _DetailNormalStrength ("Detail Normal Strength", Range(0,2)) = 0.6
        _CliffSlope ("Cliff Slope (start, end)", Vector) = (0.12, 0.3, 0, 0)
        _Gloss ("Gloss", Range(0,1)) = 0.08
        _Steps ("Toon Steps", Range(1,6)) = 3
        _Softness ("Band Softness", Range(0,1)) = 0.65
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.55
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.95
        _LocalLightStrength ("Local Light Strength", Range(0,3)) = 1
        _TerminatorWrap ("Terminator Wrap", Range(0,0.5)) = 0.05
        _RimColor ("Rim Color", Color) = (0.35,0.55,1,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.05
        _RimPower ("Rim Power", Range(0.5,8)) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Ramp); SAMPLER(sampler_Ramp);
        TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
        TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);

        CBUFFER_START(UnityPerMaterial)
            float _DetailScale;
            float _DetailStrength;
            float _DetailNormalStrength;
            float4 _CliffSlope;
            float _Gloss;
            float _Steps;
            float _Softness;
            float _AmbientBoost;
            float _ShadowStrength;
            float _LocalLightStrength;
            float _TerminatorWrap;
            float4 _RimColor;
            float _RimStrength;
            float _RimPower;
        CBUFFER_END

        float3 TriWeights(float3 n)
        {
            float3 w = pow(abs(n), 4.0);
            return w / (w.x + w.y + w.z);
        }

        float TriDetail(float3 p, float3 w)
        {
            return SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.zy).r * w.x
                 + SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.xz).r * w.y
                 + SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, p.xy).r * w.z;
        }

        float3 DecodeNormal(float2 uv, float strength)
        {
            float3 t = SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv).xyz * 2.0 - 1.0;
            t.xy *= strength;
            t.z = sqrt(saturate(1.0 - dot(t.xy, t.xy)));
            return t;
        }

        // Triplanar normal mapping with whiteout blending (object space, no tangents needed).
        float3 TriNormal(float3 p, float3 n, float3 w, float strength)
        {
            float3 tx = DecodeNormal(p.zy, strength);
            float3 ty = DecodeNormal(p.xz, strength);
            float3 tz = DecodeNormal(p.xy, strength);
            // UVs are not mirrored per side, so tangent x/y map straight onto the projected world axes.
            tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
            ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
            tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
            return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0; // x = color ramp coordinate (elevation)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS   : TEXCOORD2;
                float  ramp       : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.ramp = IN.uv.x;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nGeo = normalize(IN.normalOS);
                float3 up = normalize(IN.positionOS);
                float3 w = TriWeights(nGeo);
                float3 p = IN.positionOS / _DetailScale;
                float dist = length(IN.positionWS - _WorldSpaceCameraPos);

                // Two scales hide tiling: fine grain near, broad mottling further out. Each fades to its mean before
                // its tiles shrink to a few pixels (otherwise low mips read as a regular grid from orbit).
                float fine = (TriDetail(p, w) - 0.5) * saturate(1.0 - dist / (_DetailScale * 45.0));
                float broad = (TriDetail(p * 0.11 + 0.37, w) - 0.5) * saturate(1.0 - dist / (_DetailScale * 400.0));
                float detail = 0.5 + fine * 0.6 + broad * 0.4;

                float slope = 1.0 - dot(nGeo, up) + (detail - 0.5) * 0.08;
                float cliff = smoothstep(_CliffSlope.x, _CliffSlope.y, slope);
                float t = saturate(IN.ramp);
                float3 ground = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 0.25)).rgb;
                float3 rock = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 0.75)).rgb;
                float3 baseCol = lerp(ground, rock, cliff);
                baseCol *= 1.0 + (detail - 0.5) * 2.0 * _DetailStrength;

                // Detail normals fade out with distance to avoid shimmer from orbit.
                float nStrength = _DetailNormalStrength * saturate(1.0 - dist / (_DetailScale * 40.0)) * lerp(0.6, 1.0, cliff);
                float3 nOS = TriNormal(p, nGeo, w, nStrength);
                float3 n = normalize(TransformObjectToWorldNormal(nOS));

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float atten = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float ndl = dot(n, mainLight.direction);
                float smoothLit = smoothstep(-_TerminatorWrap, 0.65 - _TerminatorWrap * 0.6, ndl);
                float stepped = floor(smoothLit * _Steps + 0.5) / max(_Steps, 1.0);
                float lit = lerp(stepped, smoothLit, _Softness) * atten * ao.directAmbientOcclusion;

                float3 col = baseCol * mainLight.color * lit;
                col += baseCol * SampleSH(n) * _AmbientBoost * ao.indirectAmbientOcclusion;

                float3 localLight = 0;
            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, IN.positionWS);
                    float la = smoothstep(-0.1, 0.5, dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    float3 L = l.color * la;
                    localLight += L / (1.0 + L * 0.35);
                LIGHT_LOOP_END
            #endif
                col += baseCol * localLight * _LocalLightStrength;

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float ndv = saturate(dot(n, viewDir));
                float3 h = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), lerp(16.0, 96.0, _Gloss));
                col += mainLight.color * smoothstep(0.3, 0.7, spec) * _Gloss * step(0.0, ndl) * atten;

                float rim = pow(1.0 - ndv, _RimPower) * _RimStrength * saturate(lit * 2.0);
                col += lerp(mainLight.color * 0.35, _RimColor.rgb, 0.5) * rim;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(n), 0.0);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS = posCS;
                return OUT;
            }

            half4 ShadowPassFragment(Varyings IN) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DepthFrag(Varyings IN) : SV_TARGET { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
