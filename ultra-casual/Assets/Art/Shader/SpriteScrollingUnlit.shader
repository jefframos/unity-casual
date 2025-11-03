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
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;

                float4 _BaseMap_ST;       // Unity tiling/offset (kept compatible)
                float4 _Tiling;           // xy used
                float4 _Offset;           // xy used

                float4 _ScrollDir;        // xy used
                float  _ScrollSpeed;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);

                // Start with Unity's standard ST transform for sprites
                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);

                // Apply user tiling and offset, then runtime scroll
                float2 scroll = (_ScrollDir.xy) * _ScrollSpeed * _Time.y; // _Time.y = t/20, good for smooth scroll
                o.uv = uv * _Tiling.xy + _Offset.xy + scroll;

                o.color = v.color * _BaseColor;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                c *= i.color;
                // Premultiply-safe (sprite textures usually straight alpha)
                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
