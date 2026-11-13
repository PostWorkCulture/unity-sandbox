// The HUD minimap (Hud.cs): turns the map picture round the player and cuts it to a rounded square with a soft edge,
// so the minimap has no sharp corners. Drawn by Graphics.DrawTexture inside OnGUI. Px = pixels across the minimap.
Shader "Hidden/StreetLife/HudMinimap"
{
    Properties { _MainTex ("Map", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        ZTest Always ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Size;   // x, y: minimap size in px; z: corner radius px; w: edge softness px
            float4 _Map;    // x, y: map uv under the player; z, w: map uv per px across / down
            float _Rot;     // the player's yaw (radians)
            float _FlipY;   // 1 if the quad's uv.y runs top to bottom
            float4 _MapPx;  // x, y: the whole map's size in px; z: its corner radius px
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 col : COLOR; };
            v2f vert(appdata_full v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; o.col = v.color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float vy = _FlipY > .5 ? i.uv.y : 1 - i.uv.y;            // 0 at the top
                float px = (i.uv.x - .5) * _Size.x, py = (vy - .5) * _Size.y;   // px from the centre, y down
                float c = cos(_Rot), s = sin(_Rot);
                float mx = px * c - py * s, my = px * s + py * c;       // undo the map's turn (it turns by -yaw on screen)
                float2 uv = float2(_Map.x + mx * _Map.z, _Map.y - my * _Map.w);
                fixed4 col = tex2D(_MainTex, uv);
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) col.a = 0;
                // the map's own edge (a small map sits inside the minimap): rounded corners there too
                float2 mp = (uv - .5) * _MapPx.xy;
                float2 mq = abs(mp) - (_MapPx.xy * .5 - _MapPx.z);
                float md = length(max(mq, 0)) + min(max(mq.x, mq.y), 0) - _MapPx.z;
                col.a *= saturate(-md / max(_Size.w, .001));
                float2 q = abs(float2(px, py)) - (_Size.xy * .5 - _Size.z);
                float d = length(max(q, 0)) + min(max(q.x, q.y), 0) - _Size.z;   // rounded-square distance, <0 inside
                col.a *= saturate(-d / max(_Size.w, .001));
                #ifndef UNITY_COLORSPACE_GAMMA
                col.rgb = LinearToGammaSpace(col.rgb);   // IMGUI draws without sRGB conversion: match GUI.DrawTexture's colours
                #endif
                return col;
            }
            ENDCG
        }
    }
}
