Shader "Solracer/DashedLine"
{
    Properties
    {
        _Color ("Color", Color) = (0, 1, 0.5, 0.7)
        _DashLength ("Dash Length", Float) = 2.0
        _GapLength ("Gap Length", Float) = 1.0
        _LineWidth ("Line Width", Float) = 0.05
        _GlowIntensity ("Glow Intensity", Range(0, 2)) = 0.5
        _GlowSize ("Glow Size", Range(0, 1)) = 0.3
        _ScrollSpeed ("Scroll Speed", Float) = 0.0
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent" 
            "IgnoreProjector" = "True" 
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
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
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float4 color : COLOR;
            };
            
            fixed4 _Color;
            float _DashLength;
            float _GapLength;
            float _LineWidth;
            float _GlowIntensity;
            float _GlowSize;
            float _ScrollSpeed;
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.color = v.color * _Color;
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                // Calculate dash pattern based on world X position
                float patternLength = _DashLength + _GapLength;
                float scrollOffset = _Time.y * _ScrollSpeed;
                float posInPattern = fmod(i.worldPos.x + scrollOffset, patternLength);
                
                // Determine if we're in a dash or gap
                float inDash = step(posInPattern, _DashLength);
                
                // Soft edges for dash (premium feel)
                float edgeSoftness = 0.1 * _DashLength;
                float softDash = smoothstep(0, edgeSoftness, posInPattern) * 
                                 smoothstep(_DashLength, _DashLength - edgeSoftness, posInPattern);
                
                // Apply dash pattern
                float dashAlpha = inDash * softDash;
                
                // Calculate glow
                float distFromCenter = abs(i.uv.y - 0.5) * 2.0;
                float glow = (1.0 - distFromCenter) * _GlowIntensity;
                glow = pow(glow, 2.0 - _GlowSize);
                
                // Combine color with glow
                fixed4 col = i.color;
                col.rgb += col.rgb * glow;
                col.a *= dashAlpha;
                
                // Discard fully transparent pixels
                clip(col.a - 0.01);
                
                return col;
            }
            ENDCG
        }
    }
    
    FallBack "Sprites/Default"
}
