// Maps chat (Pete, 14 Nov: "on Arena the light covers the screen"): the floodlight beams. The web game draws them as flat
// see-through cones (5.5% white, normal blending); a dozen of them overlap, so from the stands or the menu camera, or standing
// inside one, they stacked into a white sheet over everything. This draws them as soft light instead: added on top, strongest
// where you look across a beam and fading at its edges (so it reads as a round shaft, not a flat sheet), fading out near the
// camera (no wash when you stand in one), and stronger up by the lamp than down at the floor.
Shader "StreetLife/Map Beam"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (0.87, 0.91, 1, 1)
        _Strength ("Strength", Float) = 0.022
        _Near ("Fade near the camera (m)", Float) = 7
        _Edge ("Edge softness", Float) = 1.6
        _Top ("Height of the lamps (m)", Float) = 30
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _Strength; half _Near; half _Edge; half _Top;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half face = pow(saturate(abs(dot(normalize(i.normalWS), v))), _Edge);
                half near = saturate((distance(_WorldSpaceCameraPos, i.positionWS) - 1.5) / max(.1, _Near));
                half up = .3 + .7 * saturate(i.positionWS.y / max(1, _Top));
                half a = _Strength * face * near * up;
                return half4(_BaseColor.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
