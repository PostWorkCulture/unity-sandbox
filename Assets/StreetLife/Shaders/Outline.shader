// Toon outline for the first-person guns (match chat), as the web game's heroOutline: back faces pushed out along
// smoothed normals (stored in UV3 by ViewModelPolish) in view space, drawn in near-black.
Shader "StreetLife/Outline"
{
    Properties
    {
        _Color ("Colour", Color) = (0.055, 0.06, 0.075, 1)
        _Width ("Width", Float) = 0.0025
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+20" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smooth : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 n = dot(i.smooth, i.smooth) > 0.01 ? i.smooth : i.normalOS;
                float3 pv = TransformWorldToView(TransformObjectToWorld(i.positionOS.xyz));
                float3 nv = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(n)));
                pv += nv * _Width;
                o.positionCS = TransformWViewToHClip(pv);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
