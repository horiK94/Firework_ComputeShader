Shader "Unlit/Firework"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
 
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                uint id : TEXCOORD0;
            };

            struct particle
            {
                float3 position;
                float3 velocity;
                float duration;
                float4 color;
                int isBomb;
            };

            StructuredBuffer<particle> _ParticleBuffer;
            
            v2f vert(appdata v, uint instanceId : SV_InstanceID)
            {
                particle p = _ParticleBuffer[instanceId];

                v.vertex.xyz *= 0.1;
                
                v2f o;
                o.vertex = p.duration <= 0 ? 0 : UnityObjectToClipPos(v.vertex + p.position);
                o.id = instanceId;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _ParticleBuffer[i.id].color;
            }
            ENDCG
        }
    }
}