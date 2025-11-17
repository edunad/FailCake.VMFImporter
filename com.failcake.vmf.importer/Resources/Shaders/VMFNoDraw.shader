Shader "FailCake/VMF/VMFNoDraw"
{
    Properties
    {
        [MaterialToggle] _CastShadows ("Cast Shadows", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }
        Pass
        {
            ColorMask 0
            ZWrite Off
        }

        // Shadow cast
        Pass
        {
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            CBUFFER_START(UnityPerMaterial)
                float _CastShadows;
            CBUFFER_END

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_CastShadows < 0.5)
                    discard;

                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}