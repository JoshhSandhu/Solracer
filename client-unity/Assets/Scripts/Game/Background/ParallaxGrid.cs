using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Creates and manages a parallax grid that moves slower than the camera.
    /// This creates the illusion of depth, making the chart feel massive.
    /// 
    /// Visual Style:
    /// - Color: Very dark grey (#2A2A35)
    /// - Opacity: 10-15%
    /// - Line Weight: Very thin (1px equivalent)
    /// </summary>
    public class ParallaxGrid : MonoBehaviour
    {
        private Color gridColor;
        private float cellSize;
        private float lineWidth;
        private Camera mainCamera;
        
        // Grid rendering
        private List<LineRenderer> horizontalLines = new List<LineRenderer>();
        private List<LineRenderer> verticalLines = new List<LineRenderer>();
        
        // Grid dimensions
        private int gridWidth = 60;  // Number of vertical lines
        private int gridHeight = 30; // Number of horizontal lines
        
        // Accumulated parallax offset
        private Vector2 parallaxOffset = Vector2.zero;
        
        // Material for grid lines
        private Material gridMaterial;
        
        private bool isInitialized = false;

        /// <summary>
        /// Initializes the parallax grid with given settings
        /// </summary>
        public void Initialize(Color color, float cellSize, float lineWidth, Camera camera)
        {
            this.gridColor = color;
            this.cellSize = cellSize;
            this.lineWidth = lineWidth;
            this.mainCamera = camera;
            
            CreateGridMaterial();
            CreateGrid();
            
            isInitialized = true;
            Debug.Log($"[ParallaxGrid] Initialized - CellSize: {cellSize}, Color: {color}");
        }

        private void CreateGridMaterial()
        {
            // Create a simple unlit material for grid lines
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
            
            Debug.Log($"[ParallaxGrid] Created grid material with color: {gridColor}, alpha: {gridColor.a}");
        }

        private void CreateGrid()
        {
            if (mainCamera == null) 
            {
                Debug.LogWarning("[ParallaxGrid] No camera assigned!");
                return;
            }
            
            // Calculate grid extent based on camera view
            float orthoSize = mainCamera.orthographicSize;
            float aspectRatio = mainCamera.aspect;
            
            // Make grid larger than visible area for seamless scrolling
            float gridExtentX = orthoSize * aspectRatio * 4f;
            float gridExtentY = orthoSize * 4f;
            
            // Calculate number of lines needed
            gridWidth = Mathf.CeilToInt(gridExtentX * 2 / cellSize) + 2;
            gridHeight = Mathf.CeilToInt(gridExtentY * 2 / cellSize) + 2;
            
            Debug.Log($"[ParallaxGrid] Creating grid: {gridWidth}x{gridHeight} lines, cellSize: {cellSize}, lineWidth: {lineWidth}");
            
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
            
            Debug.Log($"[ParallaxGrid] Grid created with {horizontalLines.Count} horizontal and {verticalLines.Count} vertical lines");
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
            lr.sortingOrder = -800; // Behind gameplay, in front of ghost candles
            lr.sortingLayerName = "Default"; // Ensure proper sorting layer
            
            // These settings help with visibility
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            
            return lr;
        }

        /// <summary>
        /// Updates the grid position based on camera movement with parallax effect
        /// </summary>
        /// <param name="cameraDelta">Change in camera position since last frame</param>
        /// <param name="parallaxSpeed">Parallax multiplier (0.15 = 15% of camera speed)</param>
        public void UpdateParallax(Vector3 cameraDelta, float parallaxSpeed)
        {
            if (!isInitialized) return;
            
            // Move grid at a fraction of camera speed (creates depth illusion)
            Vector2 movement = new Vector2(cameraDelta.x, cameraDelta.y) * parallaxSpeed;
            parallaxOffset += movement;
            
            // Apply offset to grid position
            transform.localPosition = new Vector3(
                mainCamera.transform.position.x + parallaxOffset.x,
                mainCamera.transform.position.y + parallaxOffset.y,
                transform.localPosition.z
            );
            
            // Wrap grid to prevent it from moving too far
            WrapGridIfNeeded();
        }

        /// <summary>
        /// Wraps the grid position when it moves too far from camera center
        /// This creates seamless infinite scrolling
        /// </summary>
        private void WrapGridIfNeeded()
        {
            // Calculate how far the parallax offset has accumulated
            float wrapThresholdX = cellSize * 2;
            float wrapThresholdY = cellSize * 2;
            
            // Wrap X
            if (Mathf.Abs(parallaxOffset.x) > wrapThresholdX)
            {
                parallaxOffset.x = parallaxOffset.x % wrapThresholdX;
            }
            
            // Wrap Y
            if (Mathf.Abs(parallaxOffset.y) > wrapThresholdY)
            {
                parallaxOffset.y = parallaxOffset.y % wrapThresholdY;
            }
        }

        /// <summary>
        /// Updates grid color dynamically
        /// </summary>
        public void SetGridColor(Color newColor)
        {
            gridColor = newColor;
            
            if (gridMaterial != null)
            {
                gridMaterial.color = gridColor;
            }
            
            foreach (var line in horizontalLines)
            {
                line.startColor = gridColor;
                line.endColor = gridColor;
            }
            
            foreach (var line in verticalLines)
            {
                line.startColor = gridColor;
                line.endColor = gridColor;
            }
        }

        /// <summary>
        /// Updates grid cell size (requires regeneration)
        /// </summary>
        public void SetCellSize(float newSize)
        {
            cellSize = newSize;
            RegenerateGrid();
        }

        private void RegenerateGrid()
        {
            // Clean up existing lines
            foreach (var line in horizontalLines)
            {
                if (line != null) Destroy(line.gameObject);
            }
            foreach (var line in verticalLines)
            {
                if (line != null) Destroy(line.gameObject);
            }
            horizontalLines.Clear();
            verticalLines.Clear();
            
            // Recreate
            CreateGrid();
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
