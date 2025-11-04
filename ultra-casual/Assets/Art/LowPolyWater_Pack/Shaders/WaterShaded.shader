Shader "Universal Render Pipeline/LowPolyWater/WaterShaded"
{
    Properties
    {
        _BaseColor ("Base color", Color) = (0.54, 0.95, 0.99, 0.5)
        _SpecColor ("Specular Material Color", Color) = (1,1,1,1)
        _Shininess ("Shininess", Float) = 10

        _ShoreTex ("Shore & Foam texture", 2D) = "black" {}

        // x=edge, y=shore, z=distance scale, w=unused (kept for compat)
        _InvFadeParemeter ("Auto blend parameter (Edge, Shore, Distance scale)", Vector) = (0.2, 0.39, 0.5, 1.0)

        _BumpTiling ("Foam Tiling (xyzw)", Vector) = (1.0, 1.0, -2.0, 3.0)
        _BumpDirection ("Foam movement (xyzw)", Vector) = (1.0, 1.0, -1.0, 1.0)

        // x=intensity, y=cutoff
        _Foam ("Foam (intensity, cutoff)", Vector) = (0.1, 0.375, 0, 0)

        [MaterialToggle] _isInnerAlphaBlendOrColor ("Fade inner to color (0) or alpha (1)?", Float) = 0
        _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0

            // -------------------------
            // URP pragmas / keywords
            // -------------------------
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS   // optional; we won't sum them to stay cheap
            #pragma multi_compile _ _SHADOWS_SOFT
            // Edge blend enable/disable (kept from your original)
            #pragma multi_compile _ WATER_EDGEBLEND_ON

            // -------------------------
            // Includes
            // -------------------------
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // -------------------------
            // Properties
            // -------------------------
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SpecColor;
                float   _Shininess;

                float4 _InvFadeParemeter;

                float4 _BumpTiling;
                float4 _BumpDirection;

                float4 _Foam;
                float  _isInnerAlphaBlendOrColor;
            CBUFFER_END

            // Textures
            TEXTURE2D(_ShoreTex);
            SAMPLER(sampler_ShoreTex);

            // URP depth texture
            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 bumpCoords : TEXCOORD2;
                float4 screenPos  : TEXCOORD3; // for depth sampling (ComputeScreenPos)
                half   fogFactor  : TEXCOORD4;
            };

            inline half4 Foam(TEXTURE2D_PARAM(shoreTex, shoreSampler), float4 coords)
            {
                // Same dual sample multiply, bias -0.125 as your original
                half3 a = SAMPLE_TEXTURE2D(shoreTex, shoreSampler, coords.xy).rgb;
                half3 b = SAMPLE_TEXTURE2D(shoreTex, shoreSampler, coords.zw).rgb;
                return half4(a * b - 0.125, 1);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(v.normalOS);

                // (You had space for vertex animation; leaving as identity for now)
                float4 positionCS = TransformWorldToHClip(positionWS);

                // UVs for foam movement are derived from world XZ (tileable)
                float2 uvXZ = positionWS.xz;
                float  t    = _Time.x;

                o.bumpCoords.xy = (uvXZ + t * _BumpDirection.xy) * _BumpTiling.xy;
                o.bumpCoords.zw = (uvXZ + t * _BumpDirection.zw) * _BumpTiling.zw;

                o.positionWS = positionWS;
                o.normalWS   = normalize(normalWS);
                o.positionCS = positionCS;
                o.screenPos  = ComputeScreenPos(positionCS);

                // URP fog
                o.fogFactor = ComputeFogFactor(positionCS.z);

                return o;
            }

            // Simple Blinn-Phong using URP main light
            half3 CalculateWaterLitColor(float3 positionWS, float3 normalWS, float3 viewDirWS)
            {
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 N = normalize(normalWS);
                float3 V = normalize(viewDirWS);

                // Ambient from SH
                float3 ambient = SampleSH(N) * _BaseColor.rgb;

                // Diffuse
                float ndotl = saturate(dot(N, L));
                float3 diffuse = mainLight.color * _BaseColor.rgb * ndotl;

                // Specular (Blinn)
                float3 H = normalize(L + V);
                float ndoth = saturate(dot(N, H));
                float3 spec = _SpecColor.rgb * pow(ndoth, max(1.0, _Shininess)) * ndotl * mainLight.color;

                return ambient + diffuse + spec;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // View vector
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Base lighting
                half4 baseColor = half4(CalculateWaterLitColor(i.positionWS, i.normalWS, viewDirWS), 1.0);

                // Foam
                half4 foam = Foam(_ShoreTex, sampler_ShoreTex, i.bumpCoords * 2.0);

                // Edge blending via scene depth (requires Depth Texture)
                half4 edgeBlendFactors = half4(1,0,0,0);
                #if defined(WATER_EDGEBLEND_ON)
                {
                    // screen UV
                    float2 uv = i.screenPos.xy / i.screenPos.w;

                    // Sample scene depth + convert to linear eye depth
                    float  rawDepth   = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
                    float  sceneEye   = LinearEyeDepth(rawDepth, _ZBufferParams);

                    // Current pixel eye depth:
                    // Reconstruct per-pixel depth from SV position
                    float  pixelDepth01 = (i.positionCS.z / i.positionCS.w) * 0.5f + 0.5f;
                    float  pixelEye     = LinearEyeDepth(pixelDepth01, _ZBufferParams);

                    // Keep your original remap/scale (x=edge, y=shore)
                    // edgeBlendFactors.y = 1 - saturated edge difference
                    edgeBlendFactors = saturate(_InvFadeParemeter * (sceneEye - pixelEye));
                    edgeBlendFactors.y = 1.0 - edgeBlendFactors.y;
                }
                #endif

                // Apply foam: intensity * (shore fade + cutoff test)
                baseColor.rgb += foam.rgb * _Foam.x * (edgeBlendFactors.y + saturate(_Foam.y));

                // Inner fade: to color or alpha
                if (_isInnerAlphaBlendOrColor == 0)  // to color
                {
                    baseColor.rgb += (1.0 - edgeBlendFactors.x);
                }
                else                                  // to alpha
                {
                    baseColor.a = edgeBlendFactors.x;
                }

                // Fog
                baseColor.rgb = MixFog(baseColor.rgb, i.fogFactor);

                return baseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
