Shader "Jeff/URP/BentLambert_GlobalMasked_Shadow"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _WB_DisableBend("Disable Bend (0/1)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        // -- -- -- -- -- -- -- -- -- -- -- -- -- -
        // Forward (lit + shadows)
        // -- -- -- -- -- -- -- -- -- -- -- -- -- -
        Pass
        {
            Name "ForwardBasicShadow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            // WebGL - friendly
            #pragma target 2.0
            #pragma prefer_hlslcc gles
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // URP lighting / shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            // Feature toggles you use
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER _EDGE_FADE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float _WB_DisableBend;
            CBUFFER_END

            // Globals (set via Shader.SetGlobal *)
            float _WB_Strength_G;
            float _WB_Radius_G;
            float4 _WB_Axis_G; // xyz
            float4 _WB_Origin_G; // xyz
            float _WB_EdgeFadeStartPct_G;
            float _WB_MaxYDrop_G;
            float _WB_BendStart_G;
            float _WB_BendEnd_G;
            float4 _WB_ComponentMask_G; // xyz

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float distAbs : TEXCOORD3;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // no arrays / uints
            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R * R - d * d);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                // Skip bending if disabled
                if (_WB_DisableBend > 0.5)
                {
                    OUT.positionWS = posWS;
                    OUT.positionCS = TransformWorldToHClip(posWS);
                    OUT.normalWS = normalize(nrmWS);
                    OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw; // explicit UV transform
                    OUT.distAbs = 0.0;

                    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    OUT.shadowCoord = TransformWorldToShadowCoord(posWS);
                    #endif
                    return OUT;
                }

                // Read globals
                float strength = _WB_Strength_G;
                float radius = _WB_Radius_G;
                float3 axisWS = normalize(_WB_Axis_G.xyz);
                float3 originWS = _WB_Origin_G.xyz;
                float maxDrop = _WB_MaxYDrop_G;
                float bendStart = _WB_BendStart_G;
                float bendEnd = _WB_BendEnd_G;
                float3 compMask = _WB_ComponentMask_G.xyz;

                float3 delta = (posWS - originWS) * compMask;
                float d = dot(delta, axisWS);
                float ad = abs(d);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                {
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));
                }

                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = normalize(nrmWS);
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                OUT.distAbs = ad;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                OUT.shadowCoord = TransformWorldToShadowCoord(posWS);
                #endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseCol = baseTex * _BaseColor;

                // Main light with shadows
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                Light L = GetMainLight(IN.shadowCoord);
                #else
                float4 sc = TransformWorldToShadowCoord(IN.positionWS);
                Light L = GetMainLight(sc);
                #endif

                half3 N = normalize(IN.normalWS);
                half NdotL = saturate(dot(N, L.direction));
                half3 lit = baseCol.rgb * (L.color * (NdotL * L.shadowAttenuation));
                half3 ambient = SampleSH(N) * baseCol.rgb;
                half3 color = lit + ambient;

                float fadeAlpha = 1.0;
                #if defined(_EDGE_FADE_TRANSPARENT)
                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                fadeAlpha = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                #endif

                return half4(color, baseCol.a * fadeAlpha);
            }
            ENDHLSL
        }

        // -- -- -- -- -- -- -- -- -- -- -- -- -- -
        // ShadowCaster (bent + faded)
        // -- -- -- -- -- -- -- -- -- -- -- -- -- -
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma prefer_hlslcc gles
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER _EDGE_FADE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float _WB_Strength_G;
            float _WB_Radius_G;
            float4 _WB_Axis_G;
            float4 _WB_Origin_G;
            float _WB_EdgeFadeStartPct_G;
            float _WB_MaxYDrop_G;
            float _WB_BendStart_G;
            float _WB_BendEnd_G;
            float4 _WB_ComponentMask_G;

            float _WB_DisableBend;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float distAbs : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R * R - d * d);
            }

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldDir(IN.normalOS);

                if (_WB_DisableBend > 0.5)
                {
                    OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, 0));
                    OUT.distAbs = 0;
                    return OUT;
                }

                float strength = _WB_Strength_G;
                float radius = _WB_Radius_G;
                float3 axisWS = normalize(_WB_Axis_G.xyz);
                float3 originWS = _WB_Origin_G.xyz;
                float maxDrop = _WB_MaxYDrop_G;
                float bendStart = _WB_BendStart_G;
                float bendEnd = _WB_BendEnd_G;
                float3 compMask = _WB_ComponentMask_G.xyz;

                float3 delta = (posWS - originWS) * compMask;
                float d = dot(delta, axisWS);
                float ad = abs(d);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                {
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));
                }

                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;

                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, 0));
                OUT.distAbs = ad;
                return OUT;
            }

            float4 ShadowPassFragment(Varyings IN) : SV_Target
            {
                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                float alpha = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                clip(alpha - 0.001); // keep hard clip in shadow map
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
