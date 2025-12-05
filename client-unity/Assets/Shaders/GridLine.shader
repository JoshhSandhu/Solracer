Shader "Solracer/GridLine"
{
    Properties
    {
        _Color ("Color", Color) = (0.165, 0.165, 0.21, 0.15)
        _FadeDistance ("Fade Distance", Float) = 50.0
        _MinAlpha ("Minimum Alpha", Range(0, 1)) = 0.05
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent-100" 
            "IgnoreProjector" = "True" 
            "RenderType" = "Transparent"
        }
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 worldPos : TEXCOORD0;
            };
            
            fixed4 _Color;
            float _FadeDistance;
            float _MinAlpha;
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                // Distance-based fade from camera
                float3 cameraPos = _WorldSpaceCameraPos;
                float dist = distance(i.worldPos.xyz, cameraPos);
                
                // Calculate fade factor (closer = more visible)
                float fadeFactor = saturate(1.0 - (dist / _FadeDistance));
                fadeFactor = max(fadeFactor, _MinAlpha);
                
                fixed4 col = _Color;
                col.a *= fadeFactor;
                
                return col;
            }
            ENDCG
        }
    }
    
    FallBack "Sprites/Default"
}
