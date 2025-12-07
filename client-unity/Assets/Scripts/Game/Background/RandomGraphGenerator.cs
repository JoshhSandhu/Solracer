using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Generates random graph segments with candles that scroll and loop seamlessly.
    /// Creates a dynamic trading chart background effect.
    /// </summary>
    public class RandomGraphGenerator : MonoBehaviour
    {
        private int segmentCount;
        private float segmentWidth;
        private Vector2 heightRange;
        private float scrollSpeed;
        private Color greenColor;
        private Color redColor;
        private Color graphLineColor;
        private float graphLineWidth;
        private Camera mainCamera;
        
        private List<GraphSegment> segments = new List<GraphSegment>();
        private float scrollOffset = 0f;
        
        private bool isInitialized = false;

        /// <summary>
        /// Data for a single graph segment
        /// </summary>
        private class GraphSegment
        {
            public GameObject container;
            public List<GameObject> candles = new List<GameObject>();
            public LineRenderer graphLine;
            public float startX;
            public float[] pricePoints;
        }
        

        /// <summary>
        /// Initializes the random graph generator
        /// </summary>
        public void Initialize(int count, float width, Vector2 range, float speed,
                              Color green, Color red, Camera camera)
        {
            Initialize(count, width, range, speed, green, red, camera, 
                      new Color(0.3f, 0.7f, 0.9f, 0.6f), 0.1f);
        }
        
        /// <summary>
        /// Initializes the random graph generator with graph line settings
        /// </summary>
        public void Initialize(int count, float width, Vector2 range, float speed,
                              Color green, Color red, Camera camera,
                              Color lineColor, float lineWidth)
        {
            this.segmentCount = count;
            this.segmentWidth = width;
            this.heightRange = range;
            this.scrollSpeed = speed;
            this.greenColor = green;
            this.redColor = red;
            this.graphLineColor = lineColor;
            this.graphLineWidth = lineWidth;
            this.mainCamera = camera;
            
            GenerateSegments();
            
            isInitialized = true;
            Debug.Log($"[RandomGraphGenerator] Generated {segmentCount} graph segments with connecting lines");
        }

        /// <summary>
        /// Generates all graph segments
        /// </summary>
        private void GenerateSegments()
        {
            float currentX = 0f;
            
            for (int i = 0; i < segmentCount; i++)
            {
                GraphSegment segment = new GraphSegment
                {
                    startX = currentX
                };
                
                // Generate random price points for this segment
                segment.pricePoints = GenerateRandomPricePoints(10);
                
                // Create visual representation
                CreateSegment(segment);
                
                segments.Add(segment);
                
                // Move to next segment position
                currentX += segmentWidth;
            }
        }

        /// <summary>
        /// Generates random price points for a segment
        /// </summary>
        private float[] GenerateRandomPricePoints(int count)
        {
            float[] points = new float[count];
            float currentPrice = Random.Range(heightRange.x, heightRange.y);
            
            for (int i = 0; i < count; i++)
            {
                // Random walk - price moves up or down
                float change = Random.Range(-3f, 3f);
                currentPrice += change;
                
                // Clamp to height range
                currentPrice = Mathf.Clamp(currentPrice, heightRange.x, heightRange.y);
                
                points[i] = currentPrice;
            }
            
            return points;
        }

        /// <summary>
        /// Creates visual representation of a graph segment
        /// </summary>
        private void CreateSegment(GraphSegment segment)
        {
            segment.container = new GameObject($"GraphSegment_{segments.Count}");
            segment.container.transform.SetParent(transform);
            segment.container.transform.position = new Vector3(segment.startX, 0, 0);
            
            // Create graph line connecting all price points
            CreateGraphLine(segment);
            
            // Create candles for each price point transition
            for (int i = 0; i < segment.pricePoints.Length - 1; i++)
            {
                float x = (i / (float)(segment.pricePoints.Length - 1)) * segmentWidth;
                float openPrice = segment.pricePoints[i];
                float closePrice = segment.pricePoints[i + 1];
                bool isBullish = closePrice > openPrice;
                
                CreateCandle(segment.container.transform, x, openPrice, closePrice, isBullish);
            }
        }
        
        /// <summary>
        /// Creates a line connecting all price points in a segment
        /// </summary>
        private void CreateGraphLine(GraphSegment segment)
        {
            GameObject lineObj = new GameObject("GraphLine");
            lineObj.transform.SetParent(segment.container.transform);
            lineObj.transform.localPosition = Vector3.zero;
            
            LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
            segment.graphLine = lineRenderer;
            
            // Set up line renderer
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = graphLineColor;
            lineRenderer.endColor = graphLineColor;
            lineRenderer.startWidth = graphLineWidth;
            lineRenderer.endWidth = graphLineWidth;
            lineRenderer.useWorldSpace = false;
            lineRenderer.sortingOrder = -902; // Behind candles
            lineRenderer.sortingLayerName = "Default";
            
            // Create positions array from price points
            Vector3[] positions = new Vector3[segment.pricePoints.Length];
            for (int i = 0; i < segment.pricePoints.Length; i++)
            {
                float x = (i / (float)(segment.pricePoints.Length - 1)) * segmentWidth;
                float y = segment.pricePoints[i];
                positions[i] = new Vector3(x, y, 0);
            }
            
            lineRenderer.positionCount = positions.Length;
            lineRenderer.SetPositions(positions);
            
            Debug.Log($"[RandomGraphGenerator] Created graph line with {positions.Length} points");
        }

        /// <summary>
        /// Creates a single candle at a position
        /// </summary>
        private void CreateCandle(Transform parent, float x, float open, float close, bool bullish)
        {
            GameObject candle = new GameObject($"Candle_{x:F1}");
            candle.transform.SetParent(parent);
            
            // Position candle body center
            float bodyCenter = (open + close) / 2f;
            candle.transform.localPosition = new Vector3(x, bodyCenter, 0);
            
            // Calculate candle dimensions
            float bodyHeight = Mathf.Abs(close - open);
            float bodyWidth = 0.4f;
            float wickHeight = bodyHeight * 0.3f;
            
            // Choose color based on direction
            Color color = bullish ? greenColor : redColor;
            
            // Create body (the thick part)
            GameObject body = new GameObject("Body");
            body.transform.SetParent(candle.transform);
            body.transform.localPosition = Vector3.zero;
            
            SpriteRenderer bodySr = body.AddComponent<SpriteRenderer>();
            bodySr.sprite = CreateRectSprite();
            bodySr.color = color;
            bodySr.sortingOrder = -900;
            bodySr.sortingLayerName = "Default";
            
            body.transform.localScale = new Vector3(bodyWidth, Mathf.Max(0.2f, bodyHeight), 1);
            
            // Create top wick
            GameObject topWick = new GameObject("TopWick");
            topWick.transform.SetParent(candle.transform);
            topWick.transform.localPosition = new Vector3(0, bodyHeight / 2 + wickHeight / 2, 0);
            
            SpriteRenderer topSr = topWick.AddComponent<SpriteRenderer>();
            topSr.sprite = CreateRectSprite();
            topSr.color = color;
            topSr.sortingOrder = -901;
            topSr.sortingLayerName = "Default";
            
            topWick.transform.localScale = new Vector3(bodyWidth * 0.2f, wickHeight, 1);
            
            // Create bottom wick
            GameObject bottomWick = new GameObject("BottomWick");
            bottomWick.transform.SetParent(candle.transform);
            bottomWick.transform.localPosition = new Vector3(0, -bodyHeight / 2 - wickHeight / 2, 0);
            
            SpriteRenderer bottomSr = bottomWick.AddComponent<SpriteRenderer>();
            bottomSr.sprite = CreateRectSprite();
            bottomSr.color = color;
            bottomSr.sortingOrder = -901;
            bottomSr.sortingLayerName = "Default";
            
            bottomWick.transform.localScale = new Vector3(bodyWidth * 0.2f, wickHeight, 1);
        }

        private Sprite CreateRectSprite()
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        private void Update()
        {
            if (!isInitialized) return;
            
            // Scroll graph from left to right
            scrollOffset += scrollSpeed * Time.deltaTime;
            
            // Loop when offset exceeds total segment width
            float totalWidth = segmentWidth * segmentCount;
            if (scrollOffset > totalWidth)
            {
                scrollOffset -= totalWidth;
            }
            
            // Apply scroll offset to graph
            transform.localPosition = new Vector3(-scrollOffset, 0, 0);
        }
    }
}

