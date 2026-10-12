Shader "VOIDWARD/ToonFlat"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
        [Toggle] _FlatShading ("Geometric Flat Normals", Float) = 0
        _Steps ("Toon Steps", Range(1,6)) = 3
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.6
        _RimColor ("Rim Color", Color) = (0.3,0.4,0.6,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3
        _RimStrength ("Rim Strength", Range(0,1)) = 0.25
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
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma shader_feature_local _FLATSHADING_ON
            #pragma shader_feature_local _USEVERTEXCOLOR_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fogCoord   : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Steps;
                float _AmbientBoost;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _EmissionColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
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
                baseCol *= IN.color;
            #endif

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float atten = lerp(1.0, mainLight.shadowAttenuation, 0.7);
                float lit = smoothstep(-0.15, 0.45, dot(n, mainLight.direction)) * atten;
                float stepped = floor(saturate(lit) * _Steps + 0.5) / max(_Steps, 1.0);
                stepped = lerp(0.22, 1.0, stepped);

                float3 col = baseCol.rgb * mainLight.color * stepped;
                col += baseCol.rgb * SampleSH(n) * _AmbientBoost;

            #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < lightCount; i++)
                {
                    Light l = GetAdditionalLight(i, IN.positionWS);
                    float atten = saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    col += baseCol.rgb * l.color * atten * 0.8;
                }
            #endif

                float3 viewDir = GetWorldSpaceViewDir(IN.positionWS);
                float rim = pow(1.0 - saturate(dot(n, normalize(viewDir))), _RimPower) * _RimStrength;
                col += _RimColor.rgb * rim;
                col += _EmissionColor.rgb;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1);
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
