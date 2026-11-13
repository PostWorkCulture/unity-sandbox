// Maps chat: the web game's sky dome as a skybox: a three-colour gradient (top / mid / bottom), a soft glow round the sun
// and an optional sun disc, exactly as its sky shader (applyEnv in index.html). Set per map by MapWorld from env.json.
Shader "StreetLife/Map Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.18, 0.56, 0.85, 1)
        _Mid ("Horizon", Color) = (0.53, 0.8, 0.91, 1)
        _Bot ("Below", Color) = (0.84, 0.95, 0.96, 1)
        _Ex ("Gradient exponent", Float) = 0.6
        _SunDir ("Sun direction", Vector) = (0.42, 0.85, -0.31, 0)
        _Glow ("Sun glow", Color) = (0, 0, 0, 1)
        _GlowA ("Sun glow strength", Float) = 0
        _Disc ("Sun disc", Color) = (0, 0, 0, 1)
        _DiscCos ("Sun disc cos", Float) = 1.01
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Mid, _Bot, _Glow, _Disc; float4 _SunDir; float _Ex, _GlowA, _DiscCos, _Exposure;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = i.positionOS.xyz; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.dir); float h = n.y + .04;
                float3 c = h > 0 ? lerp(_Mid.rgb, _Top.rgb, pow(saturate(h), _Ex)) : lerp(_Mid.rgb, _Bot.rgb, pow(saturate(-h * 2), .5));
                float s = max(dot(n, normalize(_SunDir.xyz)), 0);
                c += _Glow.rgb * _GlowA * (pow(s, 5) * (1 - saturate(h)) + pow(s, 60) * .8);
                c = lerp(c, _Disc.rgb, smoothstep(_DiscCos - .0006, _DiscCos + .0001, s));
                return half4(c * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
