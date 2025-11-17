Shader "Jeff/URP/BentLambert_GlobalMasked_Shadow"
{
    Properties
    {
        _BaseMap    ("Base Map", 2D) = "white" {}
        _BaseColor  ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Toon)]
        _ToonSteps  ("Toon Light Steps (per-material)", Range(1, 8)) = 3

        [Header(Color Posterize)]
        _ColorSteps ("Color Steps (per-material)", Range(0, 32)) = 0

        [Header(Outline)]
        _OutlineColor     ("Outline Color (per-material)", Color) = (0, 0, 0, 1)
        _OutlineThickness ("Outline Thickness (per-material)", Range(0, 0.1)) = 0.03

        [Header(Cutout)]
        [Toggle] _CutoutIgnore ("Ignore Global Camera Cutout", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue"      = "Transparent"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        // ---------------------------------------------------------
        // Forward (lit + shadows, toon, optional posterize)
        // ---------------------------------------------------------
        Pass
        {
            Name "ForwardBasicShadow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
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

            // Feature toggles
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER
            #pragma multi_compile _ _EDGE_FADE_TRANSPARENT
            #pragma multi_compile _ _NEAR_DITHER_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;

                float  _ToonSteps;
                float  _ColorSteps;
                float4 _OutlineColor;
                float  _OutlineThickness;

                // Per-material cutout override (0 = obey global, 1 = ignore cutout)
                float  _CutoutIgnore;
            CBUFFER_END

            // -------- GLOBALS (driven by controller) --------
            float _WB_DisableBend;
            float _WB_BendGlobe;

            float _WB_Strength_G;
            float _WB_Radius_G;
            float4 _WB_Axis_G;
            float4 _WB_Origin_G;
            float  _WB_EdgeFadeStartPct_G;
            float  _WB_MaxYDrop_G;
            float  _WB_BendStart_G;
            float  _WB_BendEnd_G;
            float4 _WB_ComponentMask_G;
            float4 _WB_TrackOffset_G;

            // Global toon / outline controls
            float4 _WB_OutlineColor_G;
            float  _WB_OutlineThickness_G;
            float  _WB_ToonSteps_G;
            float  _WB_ColorSteps_G;

            // Global near-camera dither
            float  _WB_DitherNear_G;
            float  _WB_DitherFar_G;

            // Global camera cutout
            float  _WB_CutoutEnable_G; // 0/1
            float  _WB_CutoutRadius_G; // world units on cylinder around camera->origin
            float  _WB_CutoutFade_G;   // fade width (world units) at edge of radius

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
                float  distAbs    : TEXCOORD3;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
                #endif
                float  viewDepth  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R * R - d * d);
            }

            // 4x4 Bayer dither pattern in [0,1)
            float Dither4x4(float2 pixelPos)
            {
                int x = (int)pixelPos.x & 3;
                int y = (int)pixelPos.y & 3;
                int idx = x + (y << 2);

                float threshold = 0.0;
                if      (idx == 0)  threshold = 0.0;
                else if (idx == 1)  threshold = 8.0;
                else if (idx == 2)  threshold = 2.0;
                else if (idx == 3)  threshold = 10.0;
                else if (idx == 4)  threshold = 12.0;
                else if (idx == 5)  threshold = 4.0;
                else if (idx == 6)  threshold = 14.0;
                else if (idx == 7)  threshold = 6.0;
                else if (idx == 8)  threshold = 3.0;
                else if (idx == 9)  threshold = 11.0;
                else if (idx == 10) threshold = 1.0;
                else if (idx == 11) threshold = 9.0;
                else if (idx == 12) threshold = 15.0;
                else if (idx == 13) threshold = 7.0;
                else if (idx == 14) threshold = 13.0;
                else                threshold = 5.0;

                return (threshold + 0.5) / 16.0;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                if (_WB_DisableBend > 0.5)
                {
                    OUT.positionWS = posWS;
                    OUT.positionCS = TransformWorldToHClip(posWS);
                    OUT.normalWS   = normalize(nrmWS);
                    OUT.uv         = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                    OUT.distAbs    = 0.0;

                    float3 viewPosNoBend = TransformWorldToView(posWS);
                    OUT.viewDepth = -viewPosNoBend.z;

                    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                        OUT.shadowCoord = TransformWorldToShadowCoord(posWS);
                    #endif
                    return OUT;
                }

                float  strength    = _WB_Strength_G;
                float  radius      = _WB_Radius_G;
                float3 axisWS      = normalize(_WB_Axis_G.xyz);
                float3 originWS    = _WB_Origin_G.xyz;
                float  maxDrop     = _WB_MaxYDrop_G;
                float  bendStart   = _WB_BendStart_G;
                float  bendEnd     = _WB_BendEnd_G;
                float3 compMask    = _WB_ComponentMask_G.xyz;
                float3 trackOffset = _WB_TrackOffset_G.xyz;

                float3 bendPosWS = posWS + trackOffset;

                float3 deltaRaw  = bendPosWS - originWS;
                float3 deltaMask = deltaRaw * compMask;

                float adCylinder = abs(dot(deltaMask, axisWS));

                float3 horiz = deltaRaw;
                float adGlobe = length(horiz);

                float tGlobe = saturate(_WB_BendGlobe);
                float ad = lerp(adCylinder, adGlobe, tGlobe);

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
                OUT.normalWS   = normalize(nrmWS);
                OUT.uv         = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                OUT.distAbs    = ad;

                float3 viewPos = TransformWorldToView(posWS);
                OUT.viewDepth = -viewPos.z;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    OUT.shadowCoord = TransformWorldToShadowCoord(posWS);
                #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 baseCol = baseTex * _BaseColor;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    Light L = GetMainLight(IN.shadowCoord);
                #else
                    float4 sc = TransformWorldToShadowCoord(IN.positionWS);
                    Light L = GetMainLight(sc);
                #endif

                half3 N = normalize(IN.normalWS);
                half  NdotL = saturate(dot(N, L.direction));

                // Toon steps
                half toonSteps =
                    (_WB_ToonSteps_G > 0.0)
                        ? (half)_WB_ToonSteps_G
                        : max((half)_ToonSteps, 1.0h);

                half NdotLToon = NdotL;
                if (toonSteps > 1.01h)
                {
                    NdotLToon = floor(NdotL * toonSteps) / toonSteps;
                }

                half3 lit     = baseCol.rgb * (L.color * (NdotLToon * L.shadowAttenuation));
                half3 ambient = SampleSH(N) * baseCol.rgb;
                half3 color   = lit + ambient;

                // Color posterize
                float steps = (_WB_ColorSteps_G > 0.0) ? _WB_ColorSteps_G : _ColorSteps;
                if (steps > 1.0)
                {
                    color = floor(color * steps) / steps;
                }

                float alpha = baseCol.a;

                // ----- CAMERA–PLAYER CUTOUT -----
                // Uses _WB_Origin_G as "player/world origin", and _WorldSpaceCameraPos as camera.
                if (_WB_CutoutEnable_G > 0.5 && _CutoutIgnore < 0.5 && _WB_CutoutRadius_G > 0.0)
                {
                    float3 camPos    = _WorldSpaceCameraPos;
                    float3 playerPos = _WB_Origin_G.xyz;

                    float3 camToPlayer = playerPos - camPos;
                    float segLen = length(camToPlayer);

                    if (segLen > 1e-4)
                    {
                        float3 segDir = camToPlayer / segLen;
                        float3 camToFrag = IN.positionWS - camPos;
                        float proj = dot(camToFrag, segDir);

                        // Only affect fragments between camera and player
                        if (proj > 0.0 && proj < segLen)
                        {
                            float3 closest = camPos + segDir * proj;
                            float distToLine = distance(IN.positionWS, closest);

                            float radius = _WB_CutoutRadius_G;
                            float fade   = max(_WB_CutoutFade_G, 0.0);

                            if (fade <= 0.0001)
                            {
                                if (distToLine < radius)
                                    clip(-1.0);
                            }
                            else
                            {
                                float inner = max(0.0, radius - fade);

                                if (distToLine < inner)
                                {
                                    clip(-1.0);
                                }
                                else if (distToLine < radius)
                                {
                                    float t = saturate((distToLine - inner) /
                                                       max(radius - inner, 1e-4));
                                    alpha *= t;
                                }
                            }
                        }
                    }
                }

                // ----- Environment edge fade -----
                #if defined(_EDGE_FADE_TRANSPARENT)
                    {
                        float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                        float edgeT = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                        alpha *= edgeT;
                    }
                #elif defined(_EDGE_FADE_DITHER)
                    {
                        float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                        float edgeT = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);

                        float2 screenPos = IN.positionCS.xy / IN.positionCS.w;
                        float2 pixelPos  = screenPos * _ScreenParams.xy;
                        float ditherThreshold = Dither4x4(pixelPos);

                        clip(edgeT - ditherThreshold);
                    }
                #endif

                // ----- Near-camera dither (independent) -----
                #if defined(_NEAR_DITHER_ON)
                    {
                        float nearD = _WB_DitherNear_G;
                        float farD  = max(_WB_DitherFar_G, nearD + 0.001);

                        float t = saturate((IN.viewDepth - nearD) / (farD - nearD));

                        float2 screenPosN = IN.positionCS.xy / IN.positionCS.w;
                        float2 pixelPosN  = screenPosN * _ScreenParams.xy;
                        float ditherThresholdN = Dither4x4(pixelPosN + 2.37);

                        clip(t - ditherThresholdN);
                        alpha *= t;
                    }
                #endif

                return half4(color, alpha);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------
        // Outline pass
        // ---------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma prefer_hlslcc gles
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _BEND_USE_GLOBAL
            #pragma multi_compile _ _EDGE_FADE_DITHER
            #pragma multi_compile _ _EDGE_FADE_TRANSPARENT
            #pragma multi_compile _ _NEAR_DITHER_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;

                float  _ToonSteps;
                float  _ColorSteps;
                float4 _OutlineColor;
                float  _OutlineThickness;

                float  _CutoutIgnore;
            CBUFFER_END

            float _WB_Strength_G;
            float _WB_Radius_G;
            float4 _WB_Axis_G;
            float4 _WB_Origin_G;
            float _WB_EdgeFadeStartPct_G;
            float _WB_MaxYDrop_G;
            float _WB_BendStart_G;
            float _WB_BendEnd_G;
            float4 _WB_ComponentMask_G;
            float4 _WB_TrackOffset_G;

            float _WB_DisableBend;
            float _WB_BendGlobe;

            float4 _WB_OutlineColor_G;
            float  _WB_OutlineThickness_G;
            float  _WB_ToonSteps_G;
            float  _WB_ColorSteps_G;

            float  _WB_DitherNear_G;
            float  _WB_DitherFar_G;

            // Cutout globals
            float  _WB_CutoutEnable_G;
            float  _WB_CutoutRadius_G;
            float  _WB_CutoutFade_G;

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  distAbs    : TEXCOORD1;
                float  viewDepth  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ArcSag(float dist, float radius)
            {
                float R = max(radius, 1e-3);
                float d = min(dist, R - 1e-4);
                return R - sqrt(R * R - d * d);
            }

            float Dither4x4(float2 pixelPos)
            {
                int x = (int)pixelPos.x & 3;
                int y = (int)pixelPos.y & 3;
                int idx = x + (y << 2);

                float threshold = 0.0;
                if      (idx == 0)  threshold = 0.0;
                else if (idx == 1)  threshold = 8.0;
                else if (idx == 2)  threshold = 2.0;
                else if (idx == 3)  threshold = 10.0;
                else if (idx == 4)  threshold = 12.0;
                else if (idx == 5)  threshold = 4.0;
                else if (idx == 6)  threshold = 14.0;
                else if (idx == 7)  threshold = 6.0;
                else if (idx == 8)  threshold = 3.0;
                else if (idx == 9)  threshold = 11.0;
                else if (idx == 10) threshold = 1.0;
                else if (idx == 11) threshold = 9.0;
                else if (idx == 12) threshold = 15.0;
                else if (idx == 13) threshold = 7.0;
                else if (idx == 14) threshold = 13.0;
                else                threshold = 5.0;

                return (threshold + 0.5) / 16.0;
            }

            OutlineVaryings OutlineVert(OutlineAttributes IN)
            {
                OutlineVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                float outlineThickness =
                    (_WB_OutlineThickness_G > 0.0)
                        ? _WB_OutlineThickness_G
                        : _OutlineThickness;

                posWS += nrmWS * outlineThickness;

                if (_WB_DisableBend > 0.5)
                {
                    OUT.positionWS = posWS;
                    OUT.positionCS = TransformWorldToHClip(posWS);
                    OUT.distAbs    = 0.0;

                    float3 viewPosNoBend = TransformWorldToView(posWS);
                    OUT.viewDepth = -viewPosNoBend.z;
                    return OUT;
                }

                float  strength    = _WB_Strength_G;
                float  radius      = _WB_Radius_G;
                float3 axisWS      = normalize(_WB_Axis_G.xyz);
                float3 originWS    = _WB_Origin_G.xyz;
                float  maxDrop     = _WB_MaxYDrop_G;
                float  bendStart   = _WB_BendStart_G;
                float  bendEnd     = _WB_BendEnd_G;
                float3 compMask    = _WB_ComponentMask_G.xyz;
                float3 trackOffset = _WB_TrackOffset_G.xyz;

                float3 bendPosWS = posWS + trackOffset;

                float3 deltaRaw  = bendPosWS - originWS;
                float3 deltaMask = deltaRaw * compMask;

                float adCylinder = abs(dot(deltaMask, axisWS));

                float3 horiz = deltaRaw;
                horiz.y = 0.0;
                float adGlobe = length(horiz);

                float tGlobe = saturate(_WB_BendGlobe);
                float ad = lerp(adCylinder, adGlobe, tGlobe);

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
                OUT.distAbs    = ad;

                float3 viewPos = TransformWorldToView(posWS);
                OUT.viewDepth = -viewPos.z;

                return OUT;
            }

            float4 OutlineFrag(OutlineVaryings IN) : SV_Target
            {
                float thickness =
                    (_WB_OutlineThickness_G > 0.0)
                        ? _WB_OutlineThickness_G
                        : _OutlineThickness;

                if (thickness <= 0.0001)
                {
                    clip(-1.0);
                }

                float4 col =
                    (_WB_OutlineColor_G.a != 0.0)
                        ? _WB_OutlineColor_G
                        : _OutlineColor;

                // ----- CAMERA–PLAYER CUTOUT for outline -----
                if (_WB_CutoutEnable_G > 0.5 && _CutoutIgnore < 0.5 && _WB_CutoutRadius_G > 0.0)
                {
                    float3 camPos    = _WorldSpaceCameraPos;
                    float3 playerPos = _WB_Origin_G.xyz;

                    float3 camToPlayer = playerPos - camPos;
                    float segLen = length(camToPlayer);

                    if (segLen > 1e-4)
                    {
                        float3 segDir = camToPlayer / segLen;
                        float3 camToFrag = IN.positionWS - camPos;
                        float proj = dot(camToFrag, segDir);

                        if (proj > 0.0 && proj < segLen)
                        {
                            float3 closest = camPos + segDir * proj;
                            float distToLine = distance(IN.positionWS, closest);

                            float radius = _WB_CutoutRadius_G;
                            float fade   = max(_WB_CutoutFade_G, 0.0);

                            if (fade <= 0.0001)
                            {
                                if (distToLine < radius)
                                    clip(-1.0);
                            }
                            else
                            {
                                float inner = max(0.0, radius - fade);

                                if (distToLine < inner)
                                {
                                    clip(-1.0);
                                }
                                else if (distToLine < radius)
                                {
                                    float t = saturate((distToLine - inner) /
                                                       max(radius - inner, 1e-4));
                                    col.a *= t;
                                }
                            }
                        }
                    }
                }

                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                float alphaFade = 1.0;

                if (_WB_Radius_G > 0.0 && edgeStart < _WB_Radius_G)
                {
                    alphaFade = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                }

                col.a *= alphaFade;

                // Edge dither (optional)
                #if defined(_EDGE_FADE_DITHER)
                    {
                        float2 screenPosE = IN.positionCS.xy / IN.positionCS.w;
                        float2 pixelPosE  = screenPosE * _ScreenParams.xy;
                        float ditherThresholdE = Dither4x4(pixelPosE);
                        clip(col.a - ditherThresholdE);
                    }
                #else
                    // If not dithering on edge, still discard fully transparent
                    clip(col.a - 0.001);
                #endif

                // Near-camera dither (independent)
                #if defined(_NEAR_DITHER_ON)
                    {
                        float nearD = _WB_DitherNear_G;
                        float farD  = max(_WB_DitherFar_G, nearD + 0.001);
                        float t = saturate((IN.viewDepth - nearD) / (farD - nearD));

                        float2 screenPosN = IN.positionCS.xy / IN.positionCS.w;
                        float2 pixelPosN  = screenPosN * _ScreenParams.xy;
                        float ditherThresholdN = Dither4x4(pixelPosN + 5.91);

                        clip(t - ditherThresholdN);
                        col.a *= t;
                    }
                #endif

                return col;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------
        // ShadowCaster (unchanged by cutout to avoid light-camera issues)
        // ---------------------------------------------------------
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
            #pragma multi_compile _ _EDGE_FADE_DITHER
            #pragma multi_compile _ _EDGE_FADE_TRANSPARENT

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
            float4 _WB_TrackOffset_G;

            float _WB_DisableBend;
            float _WB_BendGlobe;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  distAbs    : TEXCOORD0;
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
                    OUT.distAbs    = 0;
                    return OUT;
                }

                float  strength    = _WB_Strength_G;
                float  radius      = _WB_Radius_G;
                float3 axisWS      = normalize(_WB_Axis_G.xyz);
                float3 originWS    = _WB_Origin_G.xyz;
                float  maxDrop     = _WB_MaxYDrop_G;
                float  bendStart   = _WB_BendStart_G;
                float  bendEnd     = _WB_BendEnd_G;
                float3 compMask    = _WB_ComponentMask_G.xyz;
                float3 trackOffset = _WB_TrackOffset_G.xyz;

                float3 bendPosWS = posWS + trackOffset;

                float3 deltaRaw  = bendPosWS - originWS;
                float3 deltaMask = deltaRaw * compMask;

                float adCylinder = abs(dot(deltaMask, axisWS));

                float3 horiz = deltaRaw;
                horiz.y = 0.0;
                float adGlobe = length(horiz);

                float tGlobe = saturate(_WB_BendGlobe);
                float ad = lerp(adCylinder, adGlobe, tGlobe);

                float originFadeT = 1.0;
                if (bendEnd > bendStart)
                {
                    originFadeT = saturate((ad - bendStart) / max(1e-5, (bendEnd - bendStart)));
                }

                float sag = ArcSag(ad, radius) * strength;
                sag = min(sag, maxDrop);
                posWS.y -= sag * originFadeT;

                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, 0));
                OUT.distAbs    = ad;
                return OUT;
            }

            float4 ShadowPassFragment(Varyings IN) : SV_Target
            {
                float edgeStart = _WB_Radius_G * saturate(_WB_EdgeFadeStartPct_G);
                float alpha     = 1.0 - smoothstep(edgeStart, _WB_Radius_G, IN.distAbs);
                clip(alpha - 0.001);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
