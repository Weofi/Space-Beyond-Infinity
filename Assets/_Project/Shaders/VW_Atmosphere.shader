Shader "VOIDWARD/Atmosphere"
{
    Properties
    {
        _Color ("Atmosphere Color", Color) = (0.5,0.8,1,0.6)
        _Power ("Fresnel Power", Range(0.5,8)) = 3.5
        _Strength ("Strength", Range(0,4)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Atmo"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 posWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Power;
                float _Strength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(IN.posWS));
                float f = pow(1.0 - saturate(dot(n, v)), _Power);
                Light l = GetMainLight();
                float lit = saturate(dot(n, l.direction) * 0.6 + 0.5);
                return half4(_Color.rgb * _Strength * lit, saturate(f * _Color.a * _Strength));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
