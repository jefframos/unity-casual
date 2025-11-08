Shader "Jeff/URP/LitWorldBend"
{
    Properties
    {
        // Base PBR
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal]_NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0,2)) = 1.0
        _Metallic("Metallic", Range(0,1)) = 0.0
        _Smoothness("Smoothness", Range(0,1)) = 0.5

        // Optional alpha clip (OFF by default)
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5

        // World Bend
        _BendStrength("Bend Strength", Range(0,0.02)) = 0.0000
        _BendRadius("Bend Radius", Range(0.01, 5000)) = 2000.0
        [Toggle(_BEND_FOLLOW_CAMERA)] _BendFollowCamera("Follow Camera", Float) = 0
        _BendAxis("Fixed Bend Axis (WS)", Vector) = (0,0,1,0)
        _BendOrigin("Bend Origin (WS)", Vector) = (0,0,0,1)
        _BendMax("Max Bend Y Offset", Float) = 50.0
        _BendStart("Bend Fade Start Dist", Float) = 0.0
        _BendEnd("Bend Fade End Dist", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }
        LOD 300
        Cull Back
        ZWrite On
        ZTest LEqual

        HLSLPROGRAM
        #pragma target 3.5

        // Features
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _BEND_FOLLOW_CAMERA

        // Pipeline variants
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        #pragma multi_compile_fog
        #pragma multi_compile_instancing

        // Entry points
        #pragma vertex vert
        #pragma fragment frag

        // Includes
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

        // Textures
        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            half   _NormalScale;
            half   _Metallic;
            half   _Smoothness;
            half   _Cutoff;

            float  _BendStrength;
            float  _BendRadius;
            float4 _BendAxis;
            float4 _BendOrigin;
            float  _BendMax;
            float  _BendStart;
            float  _BendEnd;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 tangentOS  : TANGENT;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            half3  normalWS   : TEXCOORD1;
            half4  tangentWS  : TEXCOORD2; // xyz = tangent, w = sign
            half3  bitangentWS: TEXCOORD3;
            float3 positionWS : TEXCOORD4;
            UNITY_FOG_COORDS(5)
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
            #endif
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // -------- Bend helpers (same math as your working debug) --------
        float3 GetCameraForwardWS()
        {
            // Camera forward in world = inverse view * (0,0,-1)
            float3 viewFwdVS = float3(0,0,-1);
            float3x3 invView = (float3x3)UNITY_MATRIX_I_V;
            return normalize(mul(invView, viewFwdVS));
        }

        float ArcSag(float dist, float radius)
        {
            float R = max(radius, 1e-3);
            float d = min(dist, R - 1e-4);
            return R - sqrt(R*R - d*d);
        }

        float3 BendWS(float3 posWS)
        {
            if (_BendStrength <= 0.0) return posWS;

            float3 axisWS;
            #if defined(_BEND_FOLLOW_CAMERA)
                axisWS = GetCameraForwardWS();
            #else
                axisWS = normalize(_BendAxis.xyz);
            #endif

            float d = dot(posWS - _BendOrigin.xyz, axisWS);
            float ad = abs(d);

            float bendT = 1.0;
            if (_BendEnd > _BendStart)
            {
                bendT = saturate((ad - _BendStart) / max(1e-5, (_BendEnd - _BendStart)));
            }

            float sag = ArcSag(ad, _BendRadius) * _BendStrength;
            sag = min(sag, _BendMax);
            posWS.y -= sag * bendT;
            return posWS;
        }
        // ----------------------------------------------------------------

        void BuildTBN(float3 nrmWS, float4 tanWSIn, out half3 T, out half3 B, out half3 N)
        {
            N = normalize((half3)nrmWS);
            half s = tanWSIn.w * GetOddNegativeScale();
            T = normalize((half3)tanWSIn.xyz);
            B = normalize(cross(N, T) * s);
        }

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
            UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

            float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
            float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
            float4 tanWS = float4(TransformObjectToWorldDir(IN.tangentOS.xyz), IN.tangentOS.w);

            // Apply bend
            posWS = BendWS(posWS);

            OUT.positionWS = posWS;
            OUT.positionCS = TransformWorldToHClip(posWS);
            OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

            half3 T, B, N;
            BuildTBN(nrmWS, tanWS, T, B, N);
            OUT.tangentWS   = T;
            OUT.bitangentWS = B;
            OUT.normalWS    = N;

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                OUT.shadowCoord = GetShadowCoord(TransformWorldToShadowCoord(posWS));
            #endif

            UNITY_TRANSFER_FOG(OUT, OUT.positionCS);
            return OUT;
        }

        half4 frag(Varyings IN) : SV_Target
        {
            // Sample base
            half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

            #if defined(_ALPHATEST_ON)
                clip(albedo.a - _Cutoff);
            #endif

            // Normal map
            half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalScale);
            half3x3 TBN = half3x3(IN.tangentWS.xyz, IN.bitangentWS.xyz, IN.normalWS.xyz);
            half3 normalWS = normalize(mul(nTS, TBN));

            // Surface & input
            SurfaceData s;
            s.albedo     = albedo.rgb;
            s.alpha      = albedo.a;
            s.metallic   = _Metallic;
            s.smoothness = _Smoothness;
            s.normalWS   = normalWS;
            s.emission   = 0;
            s.occlusion  = 1;

            InputData i;
            ZERO_INITIALIZE(InputData, i);
            i.positionWS = IN.positionWS;
            i.normalWS   = normalWS;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                i.shadowCoord = IN.shadowCoord;
            #else
                i.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
            #endif
            i.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(IN.positionWS));
            i.fogCoord        = IN.fogCoord;
            i.bakedGI         = SAMPLE_GI(IN.uv, IN.positionWS, normalWS);

            half4 col = UniversalFragmentPBR(i, s);
            col.rgb = MixFog(col.rgb, i.fogCoord);
            return col;
        }
        ENDHLSL

        // ---------------- ShadowCaster (uses same BendWS) ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _ALPHATEST_ON

            // IMPORTANT: keep shadows stable → do NOT follow camera in shadow pass
            // (We deliberately do not enable _BEND_FOLLOW_CAMERA here.)

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                half   _Cutoff;

                float  _BendStrength;
                float  _BendRadius;
                float4 _BendAxis;
                float4 _BendOrigin;
                float  _BendMax;
                float  _BendStart;
                float  _BendEnd;
            CBUFFER_END

            // Same helpers (without camera-follow)
            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R*R - d*d);
            }

            float3 BendWS_Shadow(float3 posWS)
            {
                if (_BendStrength <= 0.0) return posWS;

                float3 axisWS = normalize(_BendAxis.xyz);
                float d = dot(posWS - _BendOrigin.xyz, axisWS);
                float ad = abs(d);

                float bendT = 1.0;
                if (_BendEnd > _BendStart)
                {
                    bendT = saturate((ad - _BendStart) / max(1e-5, (_BendEnd - _BendStart)));
                }

                float sag = ArcSag(ad, _BendRadius) * _BendStrength;
                sag = min(sag, _BendMax);
                posWS.y -= sag * bendT;
                return posWS;
            }

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
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldDir(IN.normalOS);

                posWS = BendWS_Shadow(posWS);

                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, 0));
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            float4 ShadowPassFragment(Varyings IN) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    half a = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor).a;
                    clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
