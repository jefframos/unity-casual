Shader "Jeff/URP/DebugWorldBendUnlit"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.7, 0.9, 1, 1)
        _BendStrength("Bend Strength", Range(0,0.1)) = 0.0
        _BendRadius("Bend Radius", Range(0.01, 5000)) = 2000.0
        [Toggle(_BEND_FOLLOW_CAMERA)] _BendFollowCamera("Follow Camera", Float) = 0
        _BendAxis("Fixed Bend Axis (WS)", Vector) = (0,0,1,0)
        _BendOrigin("Bend Origin (WS)", Vector) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "ForwardUnlit"
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #pragma shader_feature_local _BEND_FOLLOW_CAMERA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _BendStrength;
                float  _BendRadius;
                float4 _BendAxis;
                float4 _BendOrigin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 GetCameraForwardWS()
            {
                // Safer camera forward for URP
                // UNITY_MATRIX_V is view matrix; its third row points toward camera forward in view space.
                // Convert to world-space by using inverse view:
                float3 viewFwdVS = float3(0,0,-1);
                float3x3 invView = (float3x3)UNITY_MATRIX_I_V;
                return normalize(mul(invView, viewFwdVS));
            }

            float3 BendWS(float3 posWS)
            {
                if (_BendStrength <= 0.0) return posWS;

                float3 axisWS =
                #if defined(_BEND_FOLLOW_CAMERA)
                    GetCameraForwardWS();
                #else
                    normalize(_BendAxis.xyz);
                #endif

                float d = abs(dot(posWS - _BendOrigin.xyz, axisWS));
                float R = max(_BendRadius, 1e-3);
                d = min(d, R - 1e-4);
                // bounded circular arc sag
                float sag = (R - sqrt(R*R - d*d)) * _BendStrength;
                posWS.y -= sag;
                return posWS;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS = BendWS(posWS);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
