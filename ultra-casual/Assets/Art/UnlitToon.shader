Shader "Jeff/ToonLitWithOutline"
{
    Properties
    {
        _BaseColor     ("Base Color", Color) = (1,1,1,1)
        _MainTex       ("Albedo (optional)", 2D) = "white" {}
        [NoScaleOffset]_Ramp ("Ramp (1x256 or 256x1, Point/Clamp)", 2D) = "white" {}

        _RampSharpness ("Ramp Sharpness", Range(1,8)) = 3
        _ShadowBoost   ("Shadow Boost", Range(0,1)) = 0.0

        _SpecColor     ("Spec Color", Color) = (1,0.95,0.8,1)
        _SpecThreshold ("Spec Threshold", Range(0,1)) = 0.86
        _SpecFeather   ("Spec Feather", Range(0,0.2)) = 0.02
        _SpecIntensity ("Spec Intensity", Range(0,2)) = 0.7

        _RimColor      ("Rim Color", Color) = (1,1,1,1)
        _RimThreshold  ("Rim Threshold", Range(0,1)) = 0.7
        _RimFeather    ("Rim Feather", Range(0,0.3)) = 0.1
        _RimIntensity  ("Rim Intensity", Range(0,2)) = 0.8

        _OutlineColor  ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth  ("Outline Width", Range(0,0.02)) = 0.003

        _Cutoff        ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        HLSLINCLUDE
        // ----- Common includes
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SpecColor;
            float4 _RimColor;
            float4 _OutlineColor;

            float  _RampSharpness;
            float  _ShadowBoost;

            float  _SpecThreshold;
            float  _SpecFeather;
            float  _SpecIntensity;

            float  _RimThreshold;
            float  _RimFeather;
            float  _RimIntensity;

            float  _OutlineWidth;
            float  _Cutoff;
        CBUFFER_END

        TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
        TEXTURE2D(_Ramp);         SAMPLER(sampler_Ramp);

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
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float2 uv         : TEXCOORD2;
            float3 viewDirWS  : TEXCOORD3;
            float4 shadowCoord: TEXCOORD4; // for main light shadows
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings ToonVert(Attributes IN)
        {
            Varyings OUT;
            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

            float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
            float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

            OUT.positionCS  = TransformWorldToHClip(positionWS);
            OUT.positionWS  = positionWS;
            OUT.normalWS    = normalWS;
            OUT.uv          = IN.uv;
            OUT.viewDirWS   = GetWorldSpaceViewDir(positionWS);
            OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);
            return OUT;
        }

        // Sample horizontal ramp: x = term in [0..1]
        float3 SampleRamp(float x)
        {
            // Clamp to avoid sampling beyond edge on point/ clamp ramps
            x = saturate(x);
            float2 uv = float2(x, 0.5); // assume 1xN or Nx1 with clamp
            float3 ramp = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, uv).rgb;
            // option to sharpen bands
            ramp = pow(ramp, _RampSharpness);
            ramp = max(ramp, _ShadowBoost);
            return ramp;
        }

        // Additional lights additive diffuse (optional)
        float3 AccumulateAdditionalLights(float3 N, float3 V, float3 baseTint)
        {
            #if defined(_ADDITIONAL_LIGHTS)
            uint count = GetAdditionalLightsCount();
            float3 sum = 0;
            for (uint i = 0; i < count; i++)
            {
                Light l = GetAdditionalLight(i, 0);
                float3 L = -l.direction; // Lighting.hlsl gives light.direction from surface to light? negate for L from surface to light
                float ndotl = saturate(dot(N, normalize(L)));
                float3 ramp = SampleRamp(ndotl * l.distanceAttenuation);
                sum += baseTint * ramp * l.color;
            }
            return sum;
            #else
            return 0;
            #endif
        }
        ENDHLSL

        // ===============================
        // PASS: Forward Lit Toon
        // ===============================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex   ToonVert
            #pragma fragment ToonFrag

            // Lighting features
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            // SRP Batcher / Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            float4 ToonFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                // Albedo (optional texture * base color)
                float4 albedoTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float3 baseTint  = albedoTex.rgb * _BaseColor.rgb;

                // --- Main Light with shadows
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 L = -mainLight.direction; // light vector from surface to light
                float  ndotl = saturate(dot(N, normalize(L)));
                float  atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                float3 ramp = SampleRamp(ndotl * atten);
                float3 diffuse = baseTint * ramp * mainLight.color;

                // --- Spec band (half-vector)
                float3 H = normalize(normalize(L) + normalize(V));
                float  ndoth = saturate(dot(N, H));
                float  specBand = smoothstep(_SpecThreshold - _SpecFeather, _SpecThreshold + _SpecFeather, ndoth);
                float3 specular = _SpecColor.rgb * specBand * _SpecIntensity * mainLight.shadowAttenuation;

                // --- Rim band (view-facing edge light)
                float  fres = 1.0 - saturate(dot(N, V));
                float  rimBand = smoothstep(_RimThreshold - _RimFeather, _RimThreshold + _RimFeather, fres);
                float3 rim = _RimColor.rgb * rimBand * _RimIntensity;

                // --- Additional lights (diffuse only to keep it toony)
                float3 addLights = AccumulateAdditionalLights(N, V, baseTint);

                float3 color = diffuse + specular + rim + addLights;

                return float4(color, 1);
            }
            ENDHLSL
        }

        // ===============================
        // PASS: Outline (inverted hull)
        // ===============================
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" } // renders with forward queue
            Cull Front ZWrite On ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            struct OL_Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float Max3(float3 v) { return max(v.x, max(v.y, v.z)); }

            OL_Varyings OutlineVert(Attributes IN)
            {
                OL_Varyings OUT;

                // Compute object scale magnitude to keep width roughly constant under scaling
                float3x3 M = (float3x3)unity_ObjectToWorld;
                float3 col0 = float3(M[0][0], M[1][0], M[2][0]);
                float3 col1 = float3(M[0][1], M[1][1], M[2][1]);
                float3 col2 = float3(M[0][2], M[1][2], M[2][2]);
                float3 axisLen = float3(length(col0), length(col1), length(col2));
                float  scaleComp = rcp(Max3(axisLen) + 1e-6);

                float3 posOS = IN.positionOS.xyz;
                float3 nrmOS = normalize(IN.normalOS);

                posOS += nrmOS * (_OutlineWidth * scaleComp);

                float3 posWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            float4 OutlineFrag(OL_Varyings IN) : SV_Target
            {
                return float4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
