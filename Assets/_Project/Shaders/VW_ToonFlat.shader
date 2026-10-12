Shader "VOIDWARD/ToonFlat"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
        [Toggle] _FlatShading ("Geometric Flat Normals", Float) = 0
        _Steps ("Toon Steps", Range(1,6)) = 3
        _Softness ("Band Softness", Range(0,1)) = 0.6
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.6
        _RimColor ("Rim Color", Color) = (0.3,0.4,0.6,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3
        _RimStrength ("Rim Strength", Range(0,1)) = 0.25
        _ShadowFloor ("Shadow Floor (direct)", Range(0,0.5)) = 0.18
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.75
        _LocalLightStrength ("Local Light Strength", Range(0,3)) = 1
        _SpecStrength ("Specular Strength (x UV0.x gloss)", Range(0,3)) = 1
        _ReflStrength ("Env Reflection (x gloss^2)", Range(0,2)) = 0.8
        _GlowIntensity ("Glow Intensity (x UV0.y)", Range(0,8)) = 3
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

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
            #pragma multi_compile_instancing
            #pragma shader_feature_local _FLATSHADING_ON
            #pragma shader_feature_local _USEVERTEXCOLOR_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 surface    : TEXCOORD0; // x = gloss, y = glow (MeshBuilder)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fogCoord   : TEXCOORD2;
                float2 surface    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Steps;
                float _Softness;
                float _AmbientBoost;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _ShadowFloor;
                float _ShadowStrength;
                float _LocalLightStrength;
                float _SpecStrength;
                float _ReflStrength;
                float _GlowIntensity;
                float4 _EmissionColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.surface = IN.surface;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
            #ifdef _FLATSHADING_ON
                float3 n = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                n *= sign(dot(n, IN.normalWS) + 1e-5);
            #else
                float3 n = normalize(IN.normalWS);
            #endif

                float4 baseCol = _BaseColor;
            #ifdef _USEVERTEXCOLOR_ON
                // Vertex colors are authored in sRGB (like the UI palette) but meshes store them raw.
                #ifndef UNITY_COLORSPACE_GAMMA
                    baseCol.rgb *= SRGBToLinear(IN.color.rgb);
                #else
                    baseCol.rgb *= IN.color.rgb;
                #endif
            #endif
                float gloss = saturate(IN.surface.x);
                float glow = IN.surface.y;

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float atten = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float ndl = dot(n, mainLight.direction);
                // Soft stylized bands: a stepped ramp blended with a wrapped smooth ramp.
                float smoothLit = smoothstep(-0.25, 0.65, ndl);
                float stepped = floor(smoothLit * _Steps + 0.5) / max(_Steps, 1.0);
                float lit = lerp(stepped, smoothLit, _Softness) * atten * ao.directAmbientOcclusion;
                lit = lerp(_ShadowFloor, 1.0, lit);

                float3 col = baseCol.rgb * mainLight.color * lit;
                col += baseCol.rgb * SampleSH(n) * _AmbientBoost * ao.indirectAmbientOcclusion;

                // Local lights (flashlights, headlights, base lamps). Reinhard per light keeps close-range hits from blowing out.
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
                col += baseCol.rgb * localLight * _LocalLightStrength;

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float ndv = saturate(dot(n, viewDir));

                // Glossy plastic: crisp-edged Blinn-Phong highlight + a soft fresnel sky reflection.
                float3 h = normalize(mainLight.direction + viewDir);
                float specPow = lerp(24.0, 160.0, gloss);
                float spec = pow(saturate(dot(n, h)), specPow);
                spec = smoothstep(0.25, 0.6, spec) * gloss * _SpecStrength * step(0.0, ndl) * atten;
                col += mainLight.color * spec;
                float fres = pow(1.0 - ndv, 3.0);
                float3 refl = SampleSH(reflect(-viewDir, n));
                col += refl * (gloss * gloss) * _ReflStrength * lerp(0.25, 1.0, fres) * lerp(float3(1, 1, 1), baseCol.rgb, 0.6);

                // Rim: strongest on the unlit side so silhouettes read against black space; lit side picks up a sun-tinted edge.
                float rim = smoothstep(0.0, 1.0, pow(1.0 - ndv, _RimPower)) * _RimStrength;
                float backLit = saturate(1.0 - lit * 1.4);
                col += lerp(mainLight.color * 0.35, _RimColor.rgb, backLit) * rim * lerp(0.5, 1.0, backLit);
                col += baseCol.rgb * glow * _GlowIntensity;
                col += _EmissionColor.rgb;

                col = MixFog(col, IN.fogCoord * (1.0 - saturate(glow)));
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
            #pragma multi_compile_instancing
            #pragma shader_feature_local _FLATSHADING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
            #ifdef _FLATSHADING_ON
                float3 n = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                n *= sign(dot(n, IN.normalWS) + 1e-5);
            #else
                float3 n = normalize(IN.normalWS);
            #endif
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;
            float3 _LightPosition;

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
