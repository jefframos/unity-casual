Shader "Jeff/URP/WaterUltraCheap"
{
    Properties
    {
        [Header(Base Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.18, 0.65, 0.85, 0.85)
        _DeepColor    ("Deep Color",    Color) = (0.00, 0.20, 0.35, 0.85)

        [Header(Motion)]
        _Normal1 ("Normal 1 (RG)", 2D) = "bump" {}
        _Normal2 ("Normal 2 (RG)", 2D) = "bump" {}
        _N1Speed ("Normal1 Speed (x,y)", Vector) = (0.05, 0.03, 0, 0)
        _N2Speed ("Normal2 Speed (x,y)", Vector) = (-0.04, 0.02, 0, 0)
        _NormalStrength ("Normal Strength", Range(0,2)) = 0.6

        [Header(Fresnel)]
        _FresnelPower   ("Fresnel Power", Range(0.1,8)) = 3.0
        _FresnelBoost   ("Fresnel Boost", Range(0,1))   = 0.25

        [Header(Spec Sparkle (Fake))]
        _SpecIntensity  ("Spec Intensity", Range(0,2)) = 0.35
        _SpecSharpness  ("Spec Sharpness", Range(0.1,32)) = 8.0
        _GlitterSpeed   ("Glitter Speed", Range(0,4)) = 1.2

        _EnableDepthFade ("Enable Depth Fade (toggle)", Float) = 1
        _FoamColor    ("Foam Color", Color) = (1,1,1,1)
        _FoamDepth    ("Foam Depth (m)", Range(0.01, 3)) = 0.6
        _FoamIntensity("Foam Intensity", Range(0,2)) = 1.0
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
            Name "FORWARD_UNLIT"
            Tags{ "LightMode"="UniversalForward" }

            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            // URP libs
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float2 uv0        : TEXCOORD0;
                float4 screenPos  : TEXCOORD3;    // for depth
            };

            TEXTURE2D(_Normal1);
            SAMPLER(sampler_Normal1);
            TEXTURE2D(_Normal2);
            SAMPLER(sampler_Normal2);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;

                float4 _N1Speed;
                float4 _N2Speed;
                float  _NormalStrength;

                float  _FresnelPower;
                float  _FresnelBoost;

                float  _SpecIntensity;
                float  _SpecSharpness;
                float  _GlitterSpeed;

                float  _EnableDepthFade;
                float4 _FoamColor;
                float  _FoamDepth;
                float  _FoamIntensity;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.uv0        = v.uv0;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                return o;
            }

            // unpack RG normal from a tiling normal-like texture (cheap)
            float3 CheapNormalRG(float2 uv, TEXTURE2D_PARAM(tex, samp))
            {
                float2 rg = SAMPLE_TEXTURE2D(tex, samp, uv).rg * 2.0 - 1.0;
                float3 n = normalize(float3(rg * _NormalStrength, 1.0));
                return n;
            }

            // very cheap glittery spec using moving noise from normals
            float CheapSpecular(float3 n, float3 v, float t)
            {
                float3 h = normalize(float3(0,1,0) + v); // fake light from world up
                float ndoth = saturate(dot(n, h));
                float baseSpec = pow(ndoth, _SpecSharpness) * _SpecIntensity;

                // tiny shimmer from time
                float sparkle = frac(sin(dot(n.xy, float2(12.9898,78.233)) + t) * 43758.5453);
                return baseSpec * (0.75 + 0.25 * sparkle);
            }

            half4 frag (Varyings i) : SV_Target
            {
                // View + normal
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 upWS = float3(0,1,0);

                // Dual panning normals
                float t = _Time.y;
                float2 uv1 = i.uv0 + _N1Speed.xy * t;
                float2 uv2 = i.uv0 + _N2Speed.xy * t;

                float3 n1 = CheapNormalRG(uv1, TEXTURE2D_ARGS(_Normal1, sampler_Normal1));
                float3 n2 = CheapNormalRG(uv2, TEXTURE2D_ARGS(_Normal2, sampler_Normal2));
                float3 n  = normalize(lerp(n1, n2, 0.5));

                // Fresnel (stronger at grazing angles)
                float fresnel = pow(1.0 - saturate(dot(normalize(n), normalize(viewDirWS))), _FresnelPower);
                fresnel = saturate(fresnel + _FresnelBoost);

                // Base color blend (deep vs shallow)
                float depthFade = 0.0;
                if (_EnableDepthFade > 0.5)
                {
                    float sceneRawDepth = SampleSceneDepth(i.screenPos.xy / i.screenPos.w);
                    float sceneEye = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                    float surfEye  = LinearEyeDepth(i.positionCS.z / i.positionCS.w, _ZBufferParams);

                    float dist = max(sceneEye - surfEye, 0.0); // distance to geometry behind water
                    depthFade = saturate(1.0 - dist / _FoamDepth); // 1 near shore, 0 deep
                }

                float3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(1.0 - depthFade*0.7));
                // Add fresnel tint
                baseCol = lerp(baseCol, _ShallowColor.rgb, fresnel);

                // Fake spec
                float spec = CheapSpecular(n, viewDirWS, t * _GlitterSpeed);
                baseCol += spec;

                // Foam on contact (edges)
                float3 foam = _FoamColor.rgb * depthFade * _FoamIntensity;

                float alpha = saturate(max(_ShallowColor.a, _DeepColor.a));
                // Slightly increase alpha near edges (looks “whiter” at shore)
                alpha = saturate(alpha + depthFade * 0.25);

                return half4(baseCol + foam, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
