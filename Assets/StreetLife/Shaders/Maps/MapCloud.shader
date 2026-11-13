// Maps chat: the web game's far-off box clouds: flat colour, no fog (they sit beyond the fog's far distance, as in the web game).
Shader "StreetLife/Map Cloud"
{
    Properties { _BaseColor ("Colour", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(_BaseColor.rgb, 1); }
            ENDHLSL
        }
    }
}
