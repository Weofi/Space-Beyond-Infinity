Shader "VOIDWARD/Planet"
{
    Properties
    {
        [NoScaleOffset] _Ramp ("Color Ramp (row0 ground, row1 cliff, row2 region)", 2D) = "white" {}
        [NoScaleOffset] _DetailTex ("Detail (R, linear)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormal ("Detail Normal (linear RGB)", 2D) = "bump" {}
        _DetailScale ("Detail Scale (m)", Float) = 6
        _DetailStrength ("Detail Albedo Strength", Range(0,1)) = 0.35
        _DetailNormalStrength ("Detail Normal Strength", Range(0,2)) = 0.6
        _CliffSlope ("Cliff Slope (start, end)", Vector) = (0.12, 0.3, 0, 0)
        _RegionStrength ("Region Strength", Range(0,1)) = 0
        _PlanetCenter ("Planet Center (world)", Vector) = (0, 0, 0, 0)
        _Gloss ("Gloss", Range(0,1)) = 0.08
        _Wrap ("Light Wrap", Range(0,1)) = 0.25
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 0.45
        _MacroStrength ("Macro Variation (35 m / 200 m)", Range(0,1)) = 0.22
        _CavityStrength ("Cavity AO / Edge Light", Range(0,6)) = 2.5
        _StrataStrength ("Cliff Strata", Range(0,0.5)) = 0.14
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
            float _RegionStrength;
            float4 _PlanetCenter;
            float _Gloss;
            float _Wrap;
            float _AmbientBoost;
            float _MacroStrength;
            float _CavityStrength;
            float _StrataStrength;
            float _ShadowStrength;
            float _LocalLightStrength;
            float _TerminatorWrap;
            float4 _RimColor;
            float _RimStrength;
            float _RimPower;
        CBUFFER_END

        float Hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        // Value noise in [0,1]: cheap, seamless on the sphere (3D), never tiles like a texture.
        float VNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x),
                             lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                             lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

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
                float2 uv         : TEXCOORD0; // x = color ramp coordinate (elevation), y = region mask
                float2 uv1        : TEXCOORD1; // x = cavity (+ concave, - convex)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD2;
                float2 ramp       : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                float  cavity     : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.ramp = IN.uv;
                OUT.cavity = IN.uv1.x;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Chunks have their own origins, so all planet-space math runs relative to the planet center.
                float3 rel = IN.positionWS - _PlanetCenter.xyz;
                float3 nGeo = normalize(IN.normalWS);
                float3 up = normalize(rel);
                float3 w = TriWeights(nGeo);
                float3 p = rel / _DetailScale;
                float dist = length(IN.positionWS - _WorldSpaceCameraPos);

                // Two scales hide tiling: fine grain near, broad mottling further out. Each fades to its mean before
                // its tiles shrink to a few pixels (otherwise low mips read as a regular grid from orbit).
                float fine = (TriDetail(p, w) - 0.5) * saturate(1.0 - dist / (_DetailScale * 45.0));
                float broad = (TriDetail(p * 0.11 + 0.37, w) - 0.5) * saturate(1.0 - dist / (_DetailScale * 400.0));
                float detail = 0.5 + fine * 0.6 + broad * 0.4;

                // Macro / meso variation (#70): procedural, so it survives at every distance where the detail
                // texture has faded out. Breaks the large single-colour areas of the elevation ramp.
                float macro = (VNoise(rel / 210.0) - 0.5) + (VNoise(rel / 38.0 + 7.3) - 0.5) * 0.6;
                float slope = 1.0 - dot(nGeo, up) + (detail - 0.5) * 0.08 + macro * 0.03;
                float cliff = smoothstep(_CliffSlope.x, _CliffSlope.y, slope);
                float t = saturate(IN.ramp.x + macro * 0.035);
                float3 ground = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 1.0 / 6.0)).rgb;
                float3 region = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 5.0 / 6.0)).rgb;
                ground = lerp(ground, region, saturate(IN.ramp.y + macro * 0.25) * _RegionStrength);
                float3 rock = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t, 0.5)).rgb;
                // Layered rock on cliffs: horizontal strata bands, slightly wobbly.
                float r = length(rel);
                float strata = sin(r / 2.3 + VNoise(rel / 18.0) * 4.0) * 0.6 * saturate(1.0 - dist / 1800.0)
                             + sin(r / 0.9 + macro * 3.0) * 0.4 * saturate(1.0 - dist / 160.0);
                rock *= 1.0 + strata * _StrataStrength;
                float3 baseCol = lerp(ground, rock, cliff);
                baseCol *= 1.0 + (detail - 0.5) * 2.0 * _DetailStrength;
                baseCol *= 1.0 + macro * _MacroStrength;

                // Cavity from the mesh: concave dips darken (AO), convex crests catch a little extra light.
                float cav = IN.cavity * _CavityStrength;
                float cavityAO = saturate(1.0 - max(cav, 0.0));
                float crest = saturate(-cav);
                cavityAO = lerp(0.45, 1.0, cavityAO);

                // Detail normals fade out with distance to avoid shimmer from orbit.
                float nStrength = _DetailNormalStrength * saturate(1.0 - dist / (_DetailScale * 40.0)) * lerp(0.6, 1.0, cliff);
                float3 nWS = TriNormal(p, nGeo, w, nStrength);
                float3 n = normalize(nWS);

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float atten = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float ndl = dot(n, mainLight.direction);
                // Soft wrapped Lambert (no toon bands): smooth, readable forms like Astroneer / Outer Wilds.
                float diff = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                diff = lerp(diff, diff * diff * (3.0 - 2.0 * diff), 0.35);
                // The planet's own terminator: the night side goes dark even where slopes face the sun.
                float dayside = smoothstep(-_TerminatorWrap - 0.12, 0.12, dot(up, mainLight.direction));
                float lit = diff * atten * ao.directAmbientOcclusion * dayside * lerp(0.85, 1.0, cavityAO);

                float3 col = baseCol * mainLight.color * lit;
                float3 amb = SampleSH(n) * _AmbientBoost * ao.indirectAmbientOcclusion * cavityAO;
                col += baseCol * amb;
                col += baseCol * mainLight.color * crest * 0.18 * dayside;

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
                col += mainLight.color * spec * _Gloss * 0.6 * step(0.0, ndl) * atten * dayside;

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
