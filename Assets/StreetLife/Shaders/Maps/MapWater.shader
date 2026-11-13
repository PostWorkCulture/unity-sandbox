// Maps chat: the web game's sea (Beach Please) and lagoon water. The same wave model as the web game's GLSL moves the surface
// (a long swell steepening over the sand bar on the beach, gentle crossing ripples in the lagoon); colour comes from the
// vertices (pale turquoise shallows to deep blue), with fresnel sky reflections, a sharp sun glint, crests and foam at the
// shoreline or round the decks, and see-through shallows. Positions are converted back to the web game's axes (z negated)
// so the formulas are copied unchanged.
Shader "StreetLife/Map Water"
{
    Properties
    {
        _Mode ("Mode (0 beach, 1 lagoon)", Float) = 0
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _Gloss ("Sun glint sharpness", Float) = 900
        _Glint ("Sun glint strength", Float) = 6
        _Shallow ("Shallow opacity", Range(0, 1)) = 0.5
        _Deep ("Deep opacity", Range(0, 1)) = 0.93
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor; float _Mode, _Gloss, _Glint, _Shallow, _Deep;
            CBUFFER_END

            // ---- Beach Please: wcShore / wcPhase / wH from the web game
            static const float WK = 6.2831853 / 13., WW = 6.2831853 / 6.;
            float wSh(float z) { return 18. + 3. * sin(z * .11) + 1.5 * sin(z * .27 + 1.); }
            float wPh(float2 p, float t) { return WK * (p.x - wSh(p.y)) + WW * t + .9 * sin(p.y * .045) + .5 * sin(p.y * .11 + 1.3); }
            float wH(float2 p, float t)
            {
                float d = p.x - wSh(p.y), c = .5 + .5 * sin(wPh(p, t));
                float A = (.08 + .2 * exp(-pow((d - 6.) / 7., 2.))) * smoothstep(-.3, 3., d);
                float ch = saturate(d / 8.) * (.035 * sin(p.x * .33 + p.y * .21 - t * 1.2) + .03 * sin(-p.x * .17 + p.y * .41 + t * 1.05) + .018 * sin(p.x * .9 + p.y * .7 + t * 2.3)) * (1. + saturate((d - 30.) / 80.) * 1.5);
                return A * (pow(c, 2.5) * 1.7 - .5) + ch;
            }
            // ---- Lagoon: lgH (gentle crossing ripples, flattened far out) and lgSd (distance to the decks)
            float lgH(float2 p, float t)
            {
                float h = .16 * sin(p.x * .21 + p.y * .13 + t * .9) + .11 * sin(-p.x * .14 + p.y * .27 + t * 1.15) + .05 * sin(p.x * .9 + p.y * .7 + t * 2.3) + .03 * sin(p.x * 1.7 - p.y * 1.3 + t * 3.1);
                return h * smoothstep(70., 56., max(abs(p.x), abs(p.y)));
            }
            float sdB(float2 p, float2 c, float2 h) { float2 q = abs(p - c) - h; return length(max(q, 0.)) + min(max(q.x, q.y), 0.); }
            float lgSd(float2 p)
            {
                float a = min(sdB(p, float2(0, 14), float2(16, 8)), sdB(p, float2(0, -14), float2(16, 8)));
                float b = min(sdB(p, float2(8, 0), float2(3, 6.2)), sdB(p, float2(-8, 0), float2(3, 6.2)));
                return min(min(a, b), sdB(p, float2(12.5, 0), float2(1.5, 1.75)));
            }
            float Height(float2 p, float t) { return _Mode < .5 ? wH(p, t) : lgH(p, t); }

            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p), u = f * f * (3. - 2. * f); return lerp(lerp(h21(i), h21(i + float2(1, 0)), u.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), u.x), u.y); }
            float fbm(float2 p) { float s = 0., a = .5; for (int i = 0; i < 4; i++) { s += a * vn(p); p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p); a *= .5; } return s; }

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; half fog : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                float t = _Time.y; float2 p = float2(w.x, -w.z); const float e = .35;
                float h0 = Height(p, t), hx = Height(p + float2(e, 0), t), hz = Height(p + float2(0, e), t);
                w.y += h0;
                o.normalWS = normalize(float3(-(hx - h0) / e, 1., (hz - h0) / e));   // web z is Unity -z
                o.positionWS = w; o.positionCS = TransformWorldToHClip(w); o.color = i.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _Time.y; float3 w = i.positionWS; float2 p = float2(w.x, -w.z);
                float3 V = normalize(_WorldSpaceCameraPos - w);
                float camD = distance(_WorldSpaceCameraPos, w);
                // fine ripples on top of the swell, faded out with distance so far water doesn't shimmer
                float rk = saturate(1. - camD / 70.);
                float2 rg = float2(cos(p.x * 1.9 + p.y * .7 + t * 2.1) * .9 + cos(p.x * 3.1 - p.y * 2.3 + t * 3.3) * .5, cos(-p.x * .8 + p.y * 2.2 + t * 1.7) * .9 + cos(p.x * 2.6 + p.y * 3.4 - t * 2.9) * .5) * .06 * rk;
                float3 N = normalize(i.normalWS + float3(rg.x, 0, -rg.y));
                if (!IS_FRONT_VFACE(face, true, false)) N = -N;

                half3 col = i.color.rgb; float foam = 0, alpha;
                float n1 = fbm(p * .9 + float2(t * .05, t * .03)), n2 = fbm(p * 2.3 - float2(t * .08, -t * .06));
                if (_Mode < .5)
                {
                    float d = p.x - wSh(p.y), c = .5 + .5 * sin(wPh(p, t));
                    col = lerp(col, half3(.62, .97, .92), smoothstep(.7, 1., c) * smoothstep(1., 8., d) * .3);
                    foam = (1. - smoothstep(0., 2.6, d)) * smoothstep(.3, .7, n1 + .25);                               // lacy foam where the water meets the sand
                    foam = max(foam, smoothstep(.9, 1., c) * smoothstep(2., 5., d) * (1. - smoothstep(9., 15., d)) * smoothstep(.45, .7, n2));   // breaking crests over the bar
                    alpha = lerp(_Shallow, _Deep, smoothstep(0., 12., d));
                }
                else
                {
                    float e = lgSd(p), hh = lgH(p, t);
                    col = lerp(col, half3(.6, .97, .9), smoothstep(.05, .3, hh) * .3);
                    foam = (1. - smoothstep(.35, 1.5, e)) * smoothstep(.3, .65, n1 + .2);                             // foam lapping at the deck edges
                    foam = max(foam, smoothstep(.27, .34, hh) * smoothstep(.55, .75, n2) * .8);                          // the odd whitecap
                    alpha = lerp(_Shallow + .1, _Deep, smoothstep(.5, 22., e));
                }
                foam = saturate(foam) * (1. - saturate((camD - 120.) / 200.));

                Light L = GetMainLight(TransformWorldToShadowCoord(w));
                half sh = L.distanceAttenuation * L.shadowAttenuation;
                half3 amb = SampleSH(N);
                half3 diffuse = col * (L.color * sh * saturate(dot(N, L.direction)) * .55 + amb);
                float F = .02 + .98 * pow(1. - saturate(dot(N, V)), 5.);
                half3 refl = GlossyEnvironmentReflection(reflect(-V, N), .06, 1.);
                half3 spec = L.color * sh * pow(saturate(dot(N, normalize(L.direction + V))), _Gloss) * _Glint;
                half3 c = lerp(diffuse, refl, F) + spec;
                half3 foamC = _FoamColor.rgb * (amb + L.color * sh * .85);
                c = lerp(c, foamC, foam * .9);
                alpha = saturate(max(alpha + F * .35, foam));
                return half4(MixFog(c, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
