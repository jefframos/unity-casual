Shader "Jeff/URP/SpriteScrollingUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Sprite", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1,1,1,1)

        _Tiling     ("Tiling (x,y)", Vector) = (1,1,0,0)
        _Offset     ("Offset (x,y)", Vector) = (0,0,0,0)

        _ScrollDir  ("Scroll Dir (x,y)", Vector) = (1,0,0,0)
        _ScrollSpeed("Scroll Speed", Float) = 1.0
    }

    SubShader
    {
        Tags{
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "FORWARD"
            Tags{ "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // Read global bend values pushed by WorldBendGlobalController
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;   // keep Unity sprite ST
                float4 _Tiling;       // xy
                float4 _Offset;       // xy
                float4 _ScrollDir;    // xy
                float  _ScrollSpeed;
            CBUFFER_END

            // -------- Global bend uniforms (set by controller) --------
            float  _WB_Strength_G;
            float  _WB_Radius_G;
            float4 _WB_Axis_G;           // xyz
            float4 _WB_Origin_G;         // xyz
            float  _WB_MaxYDrop_G;
            float  _WB_BendStart_G;
            float  _WB_BendEnd_G;
            float4 _WB_ComponentMask_G;  // xyz (0/1)
            // ----------------------------------------------------------

            // Bounded circular-arc sag
            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R*R - d*d);
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // Object -> World
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                // ---- Apply same bend as the meshes (global controller) ----
                float  strength   = _WB_Strength_G;
                float  radius     = _WB_Radius_G;
                float3 axisWS     = normalize(_WB_Axis_G.xyz);
                float3 originWS   = _WB_Origin_G.xyz;
                float  maxDrop    = _WB_MaxYDrop_G;
                float  bendStart  = _WB_BendStart_G;
                float  bendEnd    = _WB_BendEnd_G;
                float3 compMask   = _WB_ComponentMask_G.xyz;

                // Mask components (e.g., only Z from origin)
                float3 delta = (posWS - originWS) * compMask;

                float d  = dot(delta, axisWS);
                float ad = abs(d);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));

                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;
                // -----------------------------------------------------------

                o.positionCS = TransformWorldToHClip(posWS);

                // Sprite UV transform + user tiling/offset + runtime scroll
                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
                float2 scroll = _ScrollDir.xy * _ScrollSpeed * _Time.y;  // _Time.y is fine for scrolling
                o.uv = uv * _Tiling.xy + _Offset.xy + scroll;

                o.color = v.color * _BaseColor;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                return c * i.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
