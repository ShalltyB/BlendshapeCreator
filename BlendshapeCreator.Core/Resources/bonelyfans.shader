Shader "Bonelyfans"
{
    Properties
    {
        _Color("Color", Color) = (0,0,0,1)
        _AlphaMask("Single Channel Alpha Mask", 2D) = "white" {}
        [MaterialToggle] _UseMaterialColor("Use Material Color", int) = 0
    }
    SubShader
    {
        Tags{ "RenderType" = "Overlay"  "Queue" = "Overlay+4000" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 _Color;
            uniform sampler2D _AlphaMask;
            int _UseMaterialColor;

            struct appdata
            {
                float4 col : COLOR;
                float4 pos : POSITION;
                float2 uv0 : TEXCOORD0;
            };

            struct v2f {
                float4 col : COLOR;
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.pos);
                o.col = v.col;
                o.uv0  = v.uv0;
                return o;
            }

            fixed3 frag(v2f i) : SV_Target
            {
                fixed3 _col = _Color.xyz * _UseMaterialColor;
                fixed3 _vert = i.col.xyz * (1 - _UseMaterialColor);
                fixed3 _mask = tex2D(_AlphaMask, i.uv0).rgb;
                return (_col + _vert) * step(0.5, _mask.r);
            }

            ENDCG
        }
    }
}