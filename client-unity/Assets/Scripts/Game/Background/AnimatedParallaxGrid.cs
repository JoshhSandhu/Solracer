using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Auto-scrolling parallax grid that loops from left to right.
    /// Used in UI scenes for animated background effect.
    /// </summary>
    public class AnimatedParallaxGrid : MonoBehaviour
    {
        private Color gridColor;
        private float cellSize;
        private float lineWidth;
        private float scrollSpeed;
        private Camera mainCamera;
        
        // Grid rendering
        private List<LineRenderer> horizontalLines = new List<LineRenderer>();
        private List<LineRenderer> verticalLines = new List<LineRenderer>();
        
        // Grid dimensions
        private int gridWidth = 60;
        private int gridHeight = 30;
        
        // Material for grid lines
        private Material gridMaterial;
        
        // Scroll offset for seamless looping
        private float scrollOffset = 0f;
        
        private bool isInitialized = false;

        /// <summary>
        /// Initializes the animated parallax grid
        /// </summary>
        public void Initialize(Color color, float cellSize, float lineWidth, float scrollSpeed, Camera camera)
        {
            this.gridColor = color;
            this.cellSize = cellSize;
            this.lineWidth = lineWidth;
            this.scrollSpeed = scrollSpeed;
            this.mainCamera = camera;
            
            CreateGridMaterial();
            CreateGrid();
            
            isInitialized = true;
            Debug.Log($"[AnimatedParallaxGrid] Initialized - ScrollSpeed: {scrollSpeed}, CellSize: {cellSize}");
        }

        private void CreateGridMaterial()
        {
            // Try to use Unlit/Color first for better visibility, fallback to Sprites/Default
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            
            gridMaterial = new Material(shader);
            gridMaterial.color = gridColor;
            
            // Enable transparency
            gridMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            gridMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            gridMaterial.renderQueue = 3000; // Transparent queue
        }

        private void CreateGrid()
        {
            if (mainCamera == null) 
            {
                Debug.LogWarning("[AnimatedParallaxGrid] No camera assigned!");
                return;
            }
            
            // Calculate grid extent based on camera view
            float orthoSize = mainCamera.orthographicSize;
            float aspectRatio = mainCamera.aspect;
            
            // Make grid larger than visible area for seamless scrolling
            float gridExtentX = orthoSize * aspectRatio * 4f;
            float gridExtentY = orthoSize * 3f;
            
            // Calculate number of lines needed
            gridWidth = Mathf.CeilToInt(gridExtentX * 2 / cellSize) + 2;
            gridHeight = Mathf.CeilToInt(gridExtentY * 2 / cellSize) + 2;
            
            // Create horizontal lines
            for (int i = 0; i < gridHeight; i++)
            {
                LineRenderer line = CreateGridLine($"HLine_{i}");
                float y = (i - gridHeight / 2) * cellSize;
                
                Vector3[] positions = new Vector3[2];
                positions[0] = new Vector3(-gridExtentX, y, 0);
                positions[1] = new Vector3(gridExtentX, y, 0);
                
                line.positionCount = 2;
                line.SetPositions(positions);
                horizontalLines.Add(line);
            }
            
            // Create vertical lines
            for (int i = 0; i < gridWidth; i++)
            {
                LineRenderer line = CreateGridLine($"VLine_{i}");
                float x = (i - gridWidth / 2) * cellSize;
                
                Vector3[] positions = new Vector3[2];
                positions[0] = new Vector3(x, -gridExtentY, 0);
                positions[1] = new Vector3(x, gridExtentY, 0);
                
                line.positionCount = 2;
                line.SetPositions(positions);
                verticalLines.Add(line);
            }
        }

        private LineRenderer CreateGridLine(string name)
        {
            GameObject lineObj = new GameObject(name);
            lineObj.transform.SetParent(transform);
            lineObj.transform.localPosition = Vector3.zero;
            
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.material = gridMaterial;
            lr.startColor = gridColor;
            lr.endColor = gridColor;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.useWorldSpace = false;
            lr.sortingOrder = -800;
            lr.sortingLayerName = "Default";
            
            // These settings help with visibility
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            
            return lr;
        }

        private void Update()
        {
            if (!isInitialized) return;
            
            // Scroll grid from left to right
            scrollOffset += scrollSpeed * Time.deltaTime;
            
            // Loop when offset exceeds cell size
            if (scrollOffset > cellSize)
            {
                scrollOffset -= cellSize;
            }
            
            // Apply scroll offset to grid position
            transform.localPosition = new Vector3(-scrollOffset, 0, 0);
        }

        private void OnDestroy()
        {
            if (gridMaterial != null)
            {
                Destroy(gridMaterial);
            }
        }
    }
}

