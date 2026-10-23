// Paper-Doll-Rüstungsfärbung: liest die drei Palettenfarben aus dem MaterialPropertyBlock
// (CharacterView.ApplyArmorColors) und färbt das Rüstungs-Sprite, ohne pro Figur ein Material zu
// instanziieren. Bewusst ein einfacher unlit Transparenz-Shader (URP-2D-kompatibel, keine Beleuchtung).
Shader "Lumiis/CharacterArmor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Color_ArmorPrimary ("Armor Primary", Color) = (1,1,1,1)
        _Color_ArmorSecondary ("Armor Secondary", Color) = (1,1,1,1)
        _Color_ArmorTrim ("Armor Trim", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _Color_ArmorPrimary;
            fixed4 _Color_ArmorSecondary;
            fixed4 _Color_ArmorTrim;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord) * i.color;
                half lum = dot(c.rgb, half3(0.299, 0.587, 0.114));

                // Dunkle Partien -> Secondary, mittlere -> Primary, helle Glanzkanten -> Trim.
                half3 palette = lerp(_Color_ArmorSecondary.rgb, _Color_ArmorPrimary.rgb, smoothstep(0.2, 0.75, lum));
                palette = lerp(palette, _Color_ArmorTrim.rgb, smoothstep(0.8, 0.98, lum));

                c.rgb *= palette;
                c.rgb *= 0.55 + 0.75 * lum;
                return c;
            }
            ENDCG
        }
    }
}
