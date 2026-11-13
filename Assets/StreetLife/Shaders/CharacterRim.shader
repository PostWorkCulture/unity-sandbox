// The web game's character rim light (charShade): a cool edge glow so fighters stand out from the scenery, strongest on
// upward-facing edges. Drawn as an extra additive pass over each character mesh (CharacterLook adds it).
// rim = uRim * (1 - N·V)^4.5 * (.25 + .75 * saturate(N.y * .7 + .5)), uRim = (.32, .36, .44)
Shader "StreetLife/CharacterRim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (0.32, 0.36, 0.44, 1)
        _Power ("Power", Float) = 4.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _Power;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rf = 1.0h - saturate(dot(n, v));
                half up = 0.25h + 0.75h * saturate(n.y * 0.7h + 0.5h);
                return half4(_RimColor.rgb * pow(rf, _Power) * up, 0);
            }
            ENDHLSL
        }
    }
}
