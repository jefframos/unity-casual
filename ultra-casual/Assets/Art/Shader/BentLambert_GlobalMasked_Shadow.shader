Shader "Jeff/URP/BentLambert_GlobalMasked_Shadow"
{
    Properties
    {
        _BaseMap  ("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Cull Back
        ZWrite Off
        ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha
        // -------------------- Forward (lit + shadows) --------------------
        Pass
        {
            Name "ForwardBasicShadow"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // Shadows & pipeline
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            // Global toggles from controller
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER _EDGE_FADE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            // Globals set by WorldBendGlobalController
            float  _WB_Strength_G;
            float  _WB_Radius_G;
            float4 _WB_Axis_G;          // xyz
            float4 _WB_Origin_G;        // xyz
            float  _WB_EdgeFadeStartPct_G;
            float  _WB_MaxYDrop_G;
            float  _WB_BendStart_G;
            float  _WB_BendEnd_G;
            float4 _WB_ComponentMask_G; // xyz (0/1)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  distAbs    : TEXCOORD3; // along-axis abs distance for edge fade
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord : TEXCOORD4;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---- helpers ----
            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R*R - d*d);
            }

            float Bayer4x4(uint2 p)
            {
                const float d[16] = {
                    0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5
                };
                uint idx = (p.y & 3) * 4 + (p.x & 3);
                return (d[idx] + 0.5) / 16.0;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                // Read globals
                float  strength   = _WB_Strength_G;
                float  radius     = _WB_Radius_G;
                float3 axisWS     = normalize(_WB_Axis_G.xyz);
                float3 originWS   = _WB_Origin_G.xyz;
                float  maxDrop    = _WB_MaxYDrop_G;
                float  bendStart  = _WB_BendStart_G;
                float  bendEnd    = _WB_BendEnd_G;
                float3 compMask   = _WB_ComponentMask_G.xyz;

                // masked delta (ignore components by zeroing them)
                float3 delta = (posWS - originWS) * compMask;

                // distance along axis (+ fade-in near bendStart..bendEnd if set)
                float d  = dot(delta, axisWS);
                float ad = abs(d);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));

                // bounded arc sag
                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;

                // write varyings
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = normalize(nrmWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.distAbs    = ad;


                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    OUT.shadowCoord = TransformWorldToShadowCoord(posWS);

                #endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
               half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
    half4 baseCol = baseTex * _BaseColor;     // include color.a * texture.a

    // lighting
    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        Light L = GetMainLight(IN.shadowCoord);
    #else
        float4 sc = TransformWorldToShadowCoord(IN.positionWS);
        Light L = GetMainLight(sc);
    #endif

    half3 N = normalize(IN.normalWS);
    half  NdotL = saturate(dot(N, L.direction));
    half3 lit = baseCol.rgb * (L.color * (NdotL * L.shadowAttenuation));
    half3 ambient = SampleSH(N) * baseCol.rgb;
    half3 color = lit + ambient;

    float fadeAlpha = 1.0;
                #if defined(_EDGE_FADE_TRANSPARENT)
                     float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                    fadeAlpha = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                #endif

                 float finalAlpha = baseCol.a * fadeAlpha;
                return half4(color, finalAlpha);
            }
            ENDHLSL
        }

        // -------------------- ShadowCaster (bent + faded) --------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // keep edge fade behavior in the shadow map
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER _EDGE_FADE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Globals (same as forward)
            float  _WB_Strength_G;
            float  _WB_Radius_G;
            float4 _WB_Axis_G;          // xyz
            float4 _WB_Origin_G;        // xyz
            float  _WB_EdgeFadeStartPct_G;
            float  _WB_MaxYDrop_G;
            float  _WB_BendStart_G;
            float  _WB_BendEnd_G;
            float4 _WB_ComponentMask_G; // xyz

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  distAbs    : TEXCOORD0; // to clip near edge
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R*R - d*d);
            }

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldDir(IN.normalOS);

                // same bend as forward
                float  strength   = _WB_Strength_G;
                float  radius     = _WB_Radius_G;
                float3 axisWS     = normalize(_WB_Axis_G.xyz);
                float3 originWS   = _WB_Origin_G.xyz;
                float  maxDrop    = _WB_MaxYDrop_G;
                float  bendStart  = _WB_BendStart_G;
                float  bendEnd    = _WB_BendEnd_G;
                float3 compMask   = _WB_ComponentMask_G.xyz;

                float3 delta = (posWS - originWS) * compMask;
                float d  = dot(delta, axisWS);
                float ad = abs(d);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));

                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;

                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, 0));
                OUT.distAbs = ad;
                return OUT;
            }

            float4 ShadowPassFragment(Varyings IN) : SV_Target
            {
                // match edge fade in shadows so shadows fade out at the horizon
                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                float alpha = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);

                #if defined(_EDGE_FADE_TRANSPARENT)
                    // hard clip at near-zero alpha to avoid lingering shadows beyond edge
                    clip(alpha - 0.001);
                #else
                    // dithered cutout inside shadow map is unnecessary; use hard clip
                    clip(alpha - 0.001);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
