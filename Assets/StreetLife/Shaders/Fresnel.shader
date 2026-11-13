// See-through rim material (match chat): the shield and thorn bubbles, and Ghost's vanish. Colour alpha is the base
// opacity, _Rim adds opacity and brightness towards the silhouette, so a vanished fighter is a faint shimmer.
Shader "StreetLife/Fresnel"
{
    Properties
    {
        _Color ("Colour (alpha = base opacity)", Color) = (1, 1, 1, 0.15)
        _Rim ("Rim", Float) = 0.8
        _Power ("Power", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "Fresnel"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Rim;
                half _Power;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rf = pow(1.0h - saturate(abs(dot(n, v))), _Power);
                half a = saturate(_Color.a + rf * _Rim);
                return half4(_Color.rgb * (0.6h + rf * 1.6h), a);
            }
            ENDHLSL
        }
    }
}
