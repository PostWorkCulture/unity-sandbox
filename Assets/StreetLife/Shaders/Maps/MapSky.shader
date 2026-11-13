// Maps chat: the sky for every map. The web game's three-colour gradient (top / horizon / below) with its sun glow and disc,
// plus Unity's own drifting cloud layer: soft fbm clouds on a dome above the map, lit from the sun's side, darker underneath,
// tinted by the sun colour at golden hour, thinning towards the horizon. Set per map by MapWorld (env.json + a few look values).
Shader "StreetLife/Map Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.18, 0.56, 0.85, 1)
        _Mid ("Horizon", Color) = (0.53, 0.8, 0.91, 1)
        _Bot ("Below", Color) = (0.84, 0.95, 0.96, 1)
        _Ex ("Gradient exponent", Float) = 0.6
        _SunDir ("Sun direction", Vector) = (0.42, 0.85, -0.31, 0)
        _SunCol ("Sun colour", Color) = (1, 0.97, 0.9, 1)
        _Glow ("Sun glow", Color) = (0, 0, 0, 1)
        _GlowA ("Sun glow strength", Float) = 0
        _Disc ("Sun disc", Color) = (0, 0, 0, 1)
        _DiscCos ("Sun disc cos", Float) = 1.01
        _Exposure ("Exposure", Float) = 1
        [Header(Clouds)]
        _CloudCover ("Cover (0 none, 1 overcast)", Range(0, 1)) = 0.45
        _CloudSharp ("Edge softness", Range(0.02, 0.6)) = 0.22
        _CloudScale ("Scale", Float) = 0.9
        _CloudSpeed ("Drift speed", Float) = 0.006
        _CloudLit ("Lit colour", Color) = (1, 1, 1, 1)
        _CloudShade ("Shadow colour", Color) = (0.62, 0.68, 0.78, 1)
        _CloudAlpha ("Opacity", Range(0, 1)) = 0.95
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
                half4 _Top, _Mid, _Bot, _Glow, _Disc, _SunCol, _CloudLit, _CloudShade; float4 _SunDir;
                float _Ex, _GlowA, _DiscCos, _Exposure, _CloudCover, _CloudSharp, _CloudScale, _CloudSpeed, _CloudAlpha;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = i.positionOS.xyz; return o; }

            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p), u = f * f * (3. - 2. * f); return lerp(lerp(h21(i), h21(i + float2(1, 0)), u.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), u.x), u.y); }
            float fbm(float2 p) { float s = 0, a = .5; [unroll] for (int i = 0; i < 6; i++) { s += a * vn(p); p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6) + 7.1; a *= .5; } return s; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.dir); float h = n.y + .04; float3 sd = normalize(_SunDir.xyz);
                float3 c = h > 0 ? lerp(_Mid.rgb, _Top.rgb, pow(saturate(h), _Ex)) : lerp(_Mid.rgb, _Bot.rgb, pow(saturate(-h * 2), .5));
                float s = max(dot(n, sd), 0);
                c += _Glow.rgb * _GlowA * (pow(s, 5) * (1 - saturate(h)) + pow(s, 60) * .8);
                c += _SunCol.rgb * pow(s, 400) * .6;                                                   // a soft halo round the sun for every map
                c = lerp(c, _Disc.rgb, smoothstep(_DiscCos - .0006, _DiscCos + .0001, s));

                if (_CloudCover > .001 && n.y > 0)
                {
                    // project onto a high flat layer so clouds shrink and pile up towards the horizon
                    float2 uv = n.xz / (n.y + .12) * _CloudScale;
                    float t = _Time.y * _CloudSpeed;
                    float2 w = uv + float2(t, t * .37);
                    float d = fbm(w) * .75 + fbm(w * 2.7 + 3.1) * .25;
                    float cov = 1 - _CloudCover;
                    float a = smoothstep(cov, cov + _CloudSharp, d);
                    // light: look a little towards the sun through the cloud; thicker = darker underneath
                    float2 toSun = normalize(sd.xz + 1e-4) * .06;
                    float d2 = fbm(w + toSun) * .75 + fbm((w + toSun) * 2.7 + 3.1) * .25;
                    float lit = saturate(.55 + (d - d2) * 6);
                    half3 cc = lerp(_CloudShade.rgb, _CloudLit.rgb, lit);
                    cc += _SunCol.rgb * pow(s, 8) * (1 - a) * .6;                                    // silver lining near the sun
                    float fade = smoothstep(.0, .18, n.y);                                           // thin out at the horizon
                    c = lerp(c, cc, a * _CloudAlpha * fade);
                }
                return half4(c * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
