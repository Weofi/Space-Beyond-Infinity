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
        _TerminatorWrap ("Terminator Wrap", Range(0,0.5)) = 0.25
        _SpecStrength ("Specular Strength (x UV0.x gloss)", Range(0,3)) = 1
        _ReflStrength ("Env Reflection (x gloss^2)", Range(0,2)) = 0.8
        _GlowIntensity ("Glow Intensity (x UV0.y)", Range(0,8)) = 3
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        [Header(Surface Detail (hero hard surfaces))]
        [Toggle] _SurfaceDetail ("Surface Detail", Float) = 0
        [NoScaleOffset] _DetailMask ("Detail Mask (R seam, G panel id, B grime, A micro)", 2D) = "black" {}
        [NoScaleOffset] _DetailNormal ("Detail Normal (panel seams, rivets)", 2D) = "bump" {}
        _PanelScale ("Panel Tiling (per metre)", Float) = 0.45
        _PanelNormalStrength ("Panel Normal Strength", Range(0,2)) = 1
        _SeamDarken ("Seam Darkening", Range(0,1)) = 0.55
        _PanelTint ("Panel Tint Variation", Range(0,0.3)) = 0.07
        _GrimeStrength ("Grime Strength", Range(0,1)) = 0.35
        _EdgeWear ("Edge Wear", Range(0,1)) = 0.6
        _EdgeHighlight ("Edge Highlight", Range(0,1)) = 0.25
        _WearColor ("Bare Metal (edge wear)", Color) = (0.62,0.66,0.72,1)
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
            #pragma shader_feature_local _SURFACEDETAIL_ON
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 surface    : TEXCOORD0; // x = gloss, y = glow, z = metal, w = edge (MeshBuilder)
                float2 detail     : TEXCOORD1; // x = panel detail strength, y = baked occlusion
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fogCoord   : TEXCOORD2;
                float4 surface    : TEXCOORD3;
                float3 positionRWS : TEXCOORD4; // camera-relative: precise derivatives far from the origin
            #ifdef _SURFACEDETAIL_ON
                float3 positionOS : TEXCOORD5;
                float3 normalOS   : TEXCOORD6;
                float2 detail     : TEXCOORD7;
            #endif
            };

            TEXTURE2D(_DetailMask);   SAMPLER(sampler_DetailMask);
            TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);

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
                float _TerminatorWrap;
                float _SpecStrength;
                float _ReflStrength;
                float _GlowIntensity;
                float4 _EmissionColor;
                float _PanelScale;
                float _PanelNormalStrength;
                float _SeamDarken;
                float _PanelTint;
                float _GrimeStrength;
                float _EdgeWear;
                float _EdgeHighlight;
                float4 _WearColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionRWS = OUT.positionWS - _WorldSpaceCameraPos;
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.surface = IN.surface;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
            #ifdef _SURFACEDETAIL_ON
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.detail = IN.detail;
            #endif
                return OUT;
            }

        #ifdef _SURFACEDETAIL_ON
            // Object-space triplanar projection of the generated panel/trim sheet. Sharp blend keeps panels on one axis.
            struct Triplanar { float2 uvX, uvY, uvZ; float3 w; };

            Triplanar MakeTriplanar(float3 p, float3 n)
            {
                Triplanar t;
                float3 a = abs(n);
                float3 w = pow(a, 8.0);
                t.w = w / max(dot(w, 1.0), 1e-5);
                // Offsets per axis so the three projections don't share seam positions.
                t.uvX = p.zy * _PanelScale + float2(0.37, 0.11);
                t.uvY = p.xz * _PanelScale + float2(0.71, 0.53);
                t.uvZ = p.xy * _PanelScale;
                return t;
            }

            float4 SampleMask(Triplanar t)
            {
                return SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, t.uvX) * t.w.x
                     + SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, t.uvY) * t.w.y
                     + SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, t.uvZ) * t.w.z;
            }

            // Whiteout/UDN triplanar normal blend (Ben Golus), done in object space.
            float3 DetailNormalOS(Triplanar t, float3 n, float strength)
            {
                float3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, t.uvX), strength);
                float3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, t.uvY), strength);
                float3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, t.uvZ), strength);
                tx = float3(tx.xy + n.zy, n.x);
                ty = float3(ty.xy + n.xz, n.y);
                tz = float3(tz.xy + n.xy, n.z);
                return normalize(tx.zyx * t.w.x + ty.xzy * t.w.y + tz.xyz * t.w.z);
            }
        #endif

            half4 frag(Varyings IN) : SV_Target
            {
            #ifdef _FLATSHADING_ON
                float3 n = normalize(cross(ddy(IN.positionRWS), ddx(IN.positionRWS)));
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
                float metal = 0;
                float occlusion = 1;
                float edgeLight = 0;

            #ifdef _SURFACEDETAIL_ON
                {
                    float panel = saturate(IN.detail.x);
                    occlusion = saturate(IN.detail.y);
                    float edge = saturate(IN.surface.w);
                    metal = saturate(IN.surface.z);
                    float3 nOS = normalize(IN.normalOS);
                    Triplanar tp = MakeTriplanar(IN.positionOS, nOS);
                    float4 mask = SampleMask(tp);
                    float seam = mask.r * panel;
                    // Panel seams, rivets and micro relief bend the normal; rubber/glow parts get none.
                    float3 dn = DetailNormalOS(tp, nOS, _PanelNormalStrength * panel);
                    n = normalize(TransformObjectToWorldNormal(dn));
                    // Per-panel tint variation and darkened seams give plates a manufactured look.
                    baseCol.rgb *= 1.0 + (mask.g - 0.5) * 2.0 * _PanelTint * panel;
                    baseCol.rgb *= 1.0 - seam * _SeamDarken;
                    occlusion *= 1.0 - seam * 0.5;
                    // Grime collects in low-frequency blotches, strongest where the vertex AO is dark.
                    float grime = saturate(mask.b * 1.6 - 0.35) * _GrimeStrength * panel * lerp(0.6, 1.0, 1.0 - occlusion);
                    baseCol.rgb *= 1.0 - grime * 0.45;
                    gloss *= 1.0 - grime * 0.6;
                    // Chipped paint on chamfered edges reveals brighter bare metal; edges also catch a little light.
                    float wear = edge * smoothstep(0.55, 0.75, mask.a + edge * 0.25) * _EdgeWear * step(metal, 0.5) * step(0.01, panel);
                    baseCol.rgb = lerp(baseCol.rgb, _WearColor.rgb, wear);
                    gloss = lerp(gloss, 0.75, wear);
                    metal = lerp(metal, 1.0, wear);
                    edgeLight = edge * _EdgeHighlight * (1.0 - wear);
                    gloss = saturate(gloss + (mask.a - 0.5) * 0.12 * panel);
                }
            #endif

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float atten = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float ndl = dot(n, mainLight.direction);
                // Soft stylized bands: a stepped ramp blended with a wrapped smooth ramp.
                float smoothLit = smoothstep(-_TerminatorWrap, 0.65 - _TerminatorWrap * 0.6, ndl);
                float stepped = floor(smoothLit * _Steps + 0.5) / max(_Steps, 1.0);
                float lit = lerp(stepped, smoothLit, _Softness) * atten * ao.directAmbientOcclusion;
                lit = lerp(_ShadowFloor, 1.0, lit);

                // Metals keep their colour in the specular, so their diffuse goes darker.
                float3 diffuseCol = baseCol.rgb * (1.0 - metal * 0.75);
                float3 col = diffuseCol * mainLight.color * lit;
                col += diffuseCol * SampleSH(n) * _AmbientBoost * ao.indirectAmbientOcclusion * occlusion;
                col += baseCol.rgb * mainLight.color * edgeLight * saturate(ndl + 0.3) * atten;

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
                col += diffuseCol * localLight * _LocalLightStrength;

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float ndv = saturate(dot(n, viewDir));
                float3 h = normalize(mainLight.direction + viewDir);

            #ifdef _SURFACEDETAIL_ON
                // Stylized PBR: GGX highlight + probe reflections. Paint has a white highlight, metal a tinted one.
                float perceptualRough = lerp(0.9, 0.06, gloss);
                float rough = max(perceptualRough * perceptualRough, 0.004);
                float3 f0 = lerp(float3(0.045, 0.045, 0.045), baseCol.rgb, metal);
                float nh = saturate(dot(n, h));
                float d = nh * nh * (rough * rough - 1.0) + 1.0;
                float ggx = (rough * rough) / (PI * d * d + 1e-5);
                float vis = 0.25 / max(lerp(saturate(ndl), 1.0, rough) * lerp(ndv, 1.0, rough), 0.05);
                float3 fSun = f0 + (1.0 - f0) * pow(1.0 - saturate(dot(viewDir, h)), 5.0);
                float3 spec = fSun * min(ggx * vis, 40.0) * saturate(ndl) * atten * _SpecStrength * lerp(0.35, 1.0, gloss);
                col += mainLight.color * spec;
                float3 fEnv = f0 + (max(1.0 - perceptualRough, f0) - f0) * pow(1.0 - ndv, 5.0);
                float3 env = GlossyEnvironmentReflection(reflect(-viewDir, n), IN.positionWS, perceptualRough, occlusion * ao.indirectAmbientOcclusion, screenUV);
                col += env * fEnv * _ReflStrength * lerp(0.25, 1.0, gloss);
            #else
                // Glossy plastic: crisp-edged Blinn-Phong highlight + a soft fresnel sky reflection.
                float specPow = lerp(24.0, 160.0, gloss);
                float spec = pow(saturate(dot(n, h)), specPow);
                spec = smoothstep(0.25, 0.6, spec) * gloss * _SpecStrength * step(0.0, ndl) * atten;
                col += mainLight.color * spec;
                float fres = pow(1.0 - ndv, 3.0);
                float3 refl = SampleSH(reflect(-viewDir, n));
                col += refl * (gloss * gloss) * _ReflStrength * lerp(0.25, 1.0, fres) * lerp(float3(1, 1, 1), baseCol.rgb, 0.6);
            #endif

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
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz) - _WorldSpaceCameraPos;
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS + _WorldSpaceCameraPos);
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
