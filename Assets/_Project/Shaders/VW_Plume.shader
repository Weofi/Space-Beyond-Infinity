Shader "VOIDWARD/Plume"
{
    // Additive engine exhaust: vertex colour (rgb tint, a = density along the plume) times a fresnel term,
    // so the cone reads as a soft glowing volume instead of a solid surface. Bloom does the rest.
    // #96: optional shock diamonds (bright bands in the core near the nozzle) and a turbulent, non-periodic flicker.
    Properties
    {
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _EdgeSoftness ("Edge Softness", Range(0.5,4)) = 1.6
        _Flicker ("Flicker", Range(0,1)) = 0.15
        _Diamonds ("Shock Diamonds", Range(0,3)) = 0
        _DiamondCount ("Diamond Count", Range(1,10)) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Additive"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float z : TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _EdgeSoftness;
                float _Flicker;
                float _Diamonds;
                float _DiamondCount;
            CBUFFER_END
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceViewDir(posWS);
                OUT.color = IN.color * _BaseColor;
                OUT.z = IN.positionOS.z;
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target
            {
                float ndv = abs(dot(normalize(IN.normalWS), normalize(IN.viewWS)));
                float edge = pow(saturate(ndv), _EdgeSoftness);
                // Travelling ripples along the plume (object z runs from 0 at the nozzle to -1 at the tip).
                float wave = 0.5 + 0.25 * sin(IN.z * 26.0 + _Time.y * 40.0) + 0.25 * sin(IN.z * 61.0 + _Time.y * 73.0 + ndv * 5.0);
                float flicker = 1.0 - _Flicker + _Flicker * wave;
                // shock diamonds: bands fixed along the plume, only in the core (facing the camera), fading out downstream
                float d = saturate(-IN.z);
                float bands = pow(saturate(0.5 + 0.5 * cos(d * _DiamondCount * 6.2831853)), 6.0);
                float diamonds = _Diamonds * bands * saturate(1.0 - d * 1.4) * smoothstep(0.35, 0.9, ndv);
                float3 rgb = IN.color.rgb * (1.0 + diamonds * 2.5);
                return half4(rgb, saturate(IN.color.a * edge * flicker * (1.0 + diamonds)));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
