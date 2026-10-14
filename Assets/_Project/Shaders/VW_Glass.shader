Shader "VOIDWARD/Glass"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (0.55,0.85,1,0.25)
        _RimColor ("Rim", Color) = (0.8,0.95,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 2.5
        _RimAlpha ("Rim Alpha", Range(0,1)) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Glass"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 posWS : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _RimPower;
                float _RimAlpha;
                float _Cull;
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
                float3 v = normalize(GetWorldSpaceViewDir(IN.posWS));
                float rim = pow(1.0 - saturate(abs(dot(normalize(IN.normalWS), v))), _RimPower);
                return half4(lerp(_BaseColor.rgb, _RimColor.rgb, rim), saturate(_BaseColor.a + rim * _RimAlpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
