using UnityEngine;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Renders a dashed line using a custom shader for premium neon effect.
    /// This creates smooth, glowing dashed lines that look professional.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class DashedLineRenderer : MonoBehaviour
    {
        [Header("Line Settings")]
        [SerializeField] private Color lineColor = new Color(0.2f, 0.9f, 0.4f, 0.7f);
        [SerializeField] private float lineWidth = 0.05f;
        
        [Header("Dash Pattern")]
        [SerializeField] private float dashLength = 2f;
        [SerializeField] private float gapLength = 1f;
        
        [Header("Glow Effect")]
        [SerializeField, Range(0f, 2f)] private float glowIntensity = 0.5f;
        [SerializeField, Range(0f, 1f)] private float glowSize = 0.3f;
        
        [Header("Animation")]
        [SerializeField] private float scrollSpeed = 0f;

        private LineRenderer lineRenderer;
        private Material dashMaterial;
        private static Shader dashedLineShader;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            CreateMaterial();
        }

        private void CreateMaterial()
        {
            // Try to find the custom shader
            if (dashedLineShader == null)
            {
                dashedLineShader = Shader.Find("Solracer/DashedLine");
            }
            
            // Fallback to default if shader not found
            if (dashedLineShader == null)
            {
                dashedLineShader = Shader.Find("Sprites/Default");
                Debug.LogWarning("[DashedLineRenderer] Custom shader not found, using fallback");
            }
            
            dashMaterial = new Material(dashedLineShader);
            UpdateMaterialProperties();
            
            lineRenderer.material = dashMaterial;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;
        }

        private void UpdateMaterialProperties()
        {
            if (dashMaterial == null) return;
            
            dashMaterial.SetColor("_Color", lineColor);
            dashMaterial.SetFloat("_DashLength", dashLength);
            dashMaterial.SetFloat("_GapLength", gapLength);
            dashMaterial.SetFloat("_LineWidth", lineWidth);
            dashMaterial.SetFloat("_GlowIntensity", glowIntensity);
            dashMaterial.SetFloat("_GlowSize", glowSize);
            dashMaterial.SetFloat("_ScrollSpeed", scrollSpeed);
        }

        /// <summary>
        /// Sets the line endpoints
        /// </summary>
        public void SetLine(Vector3 start, Vector3 end)
        {
            if (lineRenderer == null) return;
            
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
        }

        /// <summary>
        /// Sets the line color
        /// </summary>
        public void SetColor(Color color)
        {
            lineColor = color;
            
            if (lineRenderer != null)
            {
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
            }
            
            UpdateMaterialProperties();
        }

        /// <summary>
        /// Sets the dash pattern
        /// </summary>
        public void SetDashPattern(float dashLen, float gapLen)
        {
            dashLength = dashLen;
            gapLength = gapLen;
            UpdateMaterialProperties();
        }

        /// <summary>
        /// Sets glow effect parameters
        /// </summary>
        public void SetGlow(float intensity, float size)
        {
            glowIntensity = intensity;
            glowSize = size;
            UpdateMaterialProperties();
        }

        /// <summary>
        /// Enables scrolling animation
        /// </summary>
        public void SetScrollSpeed(float speed)
        {
            scrollSpeed = speed;
            UpdateMaterialProperties();
        }

        private void OnValidate()
        {
            if (dashMaterial != null)
            {
                UpdateMaterialProperties();
            }
        }

        private void OnDestroy()
        {
            if (dashMaterial != null)
            {
                Destroy(dashMaterial);
            }
        }

        /// <summary>
        /// Creates a dashed line between two points
        /// </summary>
        public static DashedLineRenderer CreateLine(Vector3 start, Vector3 end, Color color, 
                                                     float width = 0.05f, float dashLen = 2f, float gapLen = 1f)
        {
            GameObject lineObj = new GameObject("DashedLine");
            lineObj.AddComponent<LineRenderer>();
            
            DashedLineRenderer dashedLine = lineObj.AddComponent<DashedLineRenderer>();
            dashedLine.lineColor = color;
            dashedLine.lineWidth = width;
            dashedLine.dashLength = dashLen;
            dashedLine.gapLength = gapLen;
            
            dashedLine.SetLine(start, end);
            
            return dashedLine;
        }
    }
}
