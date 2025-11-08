Shader "Jeff/URP/BentLambert_GlobalMasked"
{
    Properties
    {
        _BaseMap  ("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardBasic"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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

            // Globals
            float  _WB_Strength_G;
            float  _WB_Radius_G;
            float4 _WB_Axis_G;       // xyz
            float4 _WB_Origin_G;     // xyz
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
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  distAbs    : TEXCOORD2; // along-axis abs distance for edge fade
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Helpers
            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R*R - d*d);
            }

            float Bayer4x4(uint2 p)
            {
                const float dither[16] = {
                    0,8,2,10, 12,4,14,6, 3,11,1,9, 15,7,13,5
                };
                uint idx = (p.y & 3) * 4 + (p.x & 3);
                return (dither[idx] + 0.5) / 16.0;
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

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = normalize(nrmWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.distAbs    = ad;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                // super-basic lambert
                Light L = GetMainLight();
                half NdotL = saturate(dot(normalize(IN.normalWS), L.direction));
                half3 color = baseCol * (L.color * NdotL) + SampleSH(normalize(IN.normalWS)) * baseCol;

                // Edge fade near radius
                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                float alpha = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);

                #if defined(_EDGE_FADE_TRANSPARENT)
                    return half4(color, saturate(alpha));
                #else
                    // dithered cutout (opaque)
                    uint2 pix = uint2(floor(ComputeScreenPos(IN.positionCS).xy * _ScreenParams.xy));
                    float th = Bayer4x4(pix);
                    clip(alpha - th);
                    return half4(color, 1);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
