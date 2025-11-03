using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game
{
    /// <summary>
    /// generates a 2D track from normalized data points.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(EdgeCollider2D))]
    public class TrackGenerator : MonoBehaviour
    {
        [Header("Track Settings")]
        [Tooltip("Total length of the track in world units")]
        [SerializeField] private float trackLength = 100f;

        [Tooltip("Height range of the track (min to max)")]
        [SerializeField] private Vector2 heightRange = new Vector2(-5f, 5f);

        [Tooltip("Starting X position of the track")]
        [SerializeField] private float startX = 0f;

        [Tooltip("Y offset for the track (baseline height)")]
        [SerializeField] private float yOffset = -2f;

        [Header("Visual Settings")]
        [Tooltip("Width of the track line")]
        [SerializeField] private float lineWidth = 1f;

        [Tooltip("Color of the track")]
        [SerializeField] private Color trackColor = Color.white;

        [Tooltip("Use slope-based colors (green for up, red for down)")]
        [SerializeField] private bool useSlopeColors = false;

        [Tooltip("Color when track is going up (positive slope)")]
        [SerializeField] private Color upColor = Color.green;

        [Tooltip("Color when track is going down (negative slope)")]
        [SerializeField] private Color downColor = Color.red;

        [Tooltip("Color when track is flat (zero slope)")]
        [SerializeField] private Color flatColor = Color.yellow;

        [Header("Data Settings")]
        [Tooltip("Use seed for deterministic generation")]
        [SerializeField] private bool useSeed = false;

        [Tooltip("Seed value (only used if useSeed is true)")]
        [SerializeField] private int seed = 42;

        [Header("Smoothing")]
        [Tooltip("Smooth the track points (reduces sharp edges)")]
        [SerializeField] private bool smoothTrack = true;

        [Tooltip("Smoothing factor (higher = smoother)")]
        [SerializeField] [Range(0f, 1f)] private float smoothingFactor = 0.5f;

        [Header("Point Density")]
        [Tooltip("Point spacing multiplier (higher = more spread out points, smoother slopes)")]
        [SerializeField] [Range(1, 10)] private int pointDensityMultiplier = 1;

        [Tooltip("Maximum number of points to use (0 = use all points from data provider)")]
        [SerializeField] private int maxPoints = 0;

        // Components
        private LineRenderer lineRenderer;
        private EdgeCollider2D edgeCollider;
        private float[] trackData;
        private Vector2[] trackPoints;
        private Vector2[] smoothedPoints;

        //properties
        //Gets the generated track points in world space.
        public Vector2[] TrackPoints => smoothedPoints ?? trackPoints;

        //gets the number of track points generated
        public int PointCount => trackPoints != null ? trackPoints.Length : 0;

        //gets the track length.
        public float TrackLength => trackLength;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            edgeCollider = GetComponent<EdgeCollider2D>();

            if (lineRenderer == null)
            {
                Debug.LogError("TrackGenerator: LineRenderer component not found!");
                enabled = false;
                return;
            }

            if (edgeCollider == null)
            {
                Debug.LogError("TrackGenerator: EdgeCollider2D component not found!");
                enabled = false;
                return;
            }
        }

        private void Start()
        {
            GenerateTrack();
        }

        //generates the track from mock data
        public void GenerateTrack()
        {
            //get track data
            if (useSeed)
            {
                trackData = TrackDataProvider.GetMockTrackDataWithSeed(seed);
            }
            else
            {
                trackData = TrackDataProvider.GetMockTrackData();
            }

            //generate world space points
            trackPoints = GenerateWorldPoints(trackData);

            //apply smoothing if enabled
            if (smoothTrack && smoothingFactor > 0f)
            {
                smoothedPoints = SmoothPoints(trackPoints, smoothingFactor);
            }
            else
            {
                smoothedPoints = trackPoints;
            }

            SetupLineRenderer();
            SetupEdgeCollider();

            Debug.Log($"TrackGenerator: Generated track with {trackPoints.Length} points (from {trackData.Length} data points), length: {trackLength}");
        }

        //generate world space points from normalized track data.
        private Vector2[] GenerateWorldPoints(float[] normalizedData)
        {
            if (normalizedData == null || normalizedData.Length == 0)
            {
                Debug.LogError("TrackGenerator: No track data provided!");
                return new Vector2[0];
            }

            //Apply point density: skip points to create more spread out track
            int effectivePointCount = normalizedData.Length;
            
            //If maxPoints is set and less than total, use that
            if (maxPoints > 0 && maxPoints < effectivePointCount)
            {
                effectivePointCount = maxPoints;
            }
            
            //Apply density multiplier to skip points (makes points more spread out)
            if (pointDensityMultiplier > 1)
            {
                effectivePointCount = Mathf.Max(2, effectivePointCount / pointDensityMultiplier);
            }

            Vector2[] points = new Vector2[effectivePointCount];
            
            //Calculate step size based on effective point count
            float step = trackLength / (effectivePointCount - 1);
            
            //Sample points from normalized data
            for (int i = 0; i < effectivePointCount; i++)
            {
                //Map current index to original data array index
                int sourceIndex = 0;
                if (effectivePointCount > 1)
                {
                    sourceIndex = Mathf.RoundToInt((float)i / (effectivePointCount - 1) * (normalizedData.Length - 1));
                    sourceIndex = Mathf.Clamp(sourceIndex, 0, normalizedData.Length - 1);
                }
                
                float x = startX + (i * step);
                float normalizedHeight = normalizedData[sourceIndex];  //height value between 0 and 1
                float y = Mathf.Lerp(heightRange.x, heightRange.y, normalizedHeight) + yOffset;

                points[i] = new Vector2(x, y);
            }
            
            return points;
        }

        //smooths track points to reduce sharp edges.
        private Vector2[] SmoothPoints(Vector2[] points, float factor)
        {
            if (points == null || points.Length < 3)
                return points;

            Vector2[] smoothed = new Vector2[points.Length];
            smoothed[0] = points[0];
            smoothed[points.Length - 1] = points[points.Length - 1];

            //apply smoothing to middle points
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 prev = points[i - 1];
                Vector2 current = points[i];
                Vector2 next = points[i + 1];
                Vector2 averaged = (prev + current + next) / 3f;
                smoothed[i] = Vector2.Lerp(current, averaged, factor);
            }
            return smoothed;
        }

        //sets up LineRenderer for visual track rendering
        private void SetupLineRenderer()
        {
            if (lineRenderer == null || smoothedPoints == null)
                return;

            lineRenderer.positionCount = smoothedPoints.Length;
            lineRenderer.SetPositions(ConvertToVector3Array(smoothedPoints));
            
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.useWorldSpace = true;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.sortingOrder = 0;

            //apply colors based on slope or use single color
            if (useSlopeColors && smoothedPoints.Length > 1)
            {
                SetupSlopeColors();
            }
            else
            {
                lineRenderer.startColor = trackColor;
                lineRenderer.endColor = trackColor;
            }
        }

        //sets up per-vertex colors based on track slope
        private void SetupSlopeColors()
        {
            if (smoothedPoints == null || smoothedPoints.Length < 2)
                return;

            Color[] colors = new Color[smoothedPoints.Length];

            //calculate color for each point based on slope
            for (int i = 0; i < smoothedPoints.Length; i++)
            {
                float slope = 0f;

                if (i == 0)
                {
                    //first point: use slope to next point
                    Vector2 delta = smoothedPoints[i + 1] - smoothedPoints[i];
                    slope = delta.y / delta.x;
                }
                else if (i == smoothedPoints.Length - 1)
                {
                    //last point: use slope from previous point
                    Vector2 delta = smoothedPoints[i] - smoothedPoints[i - 1];
                    slope = delta.y / delta.x;
                }
                else
                {
                    //middle points: use average slope from previous and next
                    Vector2 delta1 = smoothedPoints[i] - smoothedPoints[i - 1];
                    Vector2 delta2 = smoothedPoints[i + 1] - smoothedPoints[i];
                    float slope1 = delta1.y / delta1.x;
                    float slope2 = delta2.y / delta2.x;
                    slope = (slope1 + slope2) / 2f;
                }

                //map slope to color: positive = green, negative = red, zero = yellow
                if (slope > 0.01f)
                {
                    //going up: green
                    colors[i] = upColor;
                }
                else if (slope < -0.01f)
                {
                    //going down: red
                    colors[i] = downColor;
                }
                else
                {
                    //flat: yellow
                    colors[i] = flatColor;
                }
            }

            //apply colors to LineRenderer
            lineRenderer.colorGradient = CreateGradientFromColors(colors);
        }

        //creates a gradient from color array for LineRenderer
        //Unity Gradient supports max 8 color keys, so we sample intelligently
        private Gradient CreateGradientFromColors(Color[] colors)
        {
            if (colors == null || colors.Length == 0)
            {
                //fallback to simple gradient
                Gradient g = new Gradient();
                g.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(trackColor, 0f), new GradientColorKey(trackColor, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
                return g;
            }

            const int maxKeys = 8; //Unity's Gradient limit
            
            //if we have fewer colors than max, use all
            if (colors.Length <= maxKeys)
            {
                GradientColorKey[] colorKey = new GradientColorKey[colors.Length];
                GradientAlphaKey[] alphaKey = new GradientAlphaKey[colors.Length];

                for (int i = 0; i < colors.Length; i++)
                {
                    float time = colors.Length > 1 ? (float)i / (colors.Length - 1) : 0f;
                    colorKey[i] = new GradientColorKey(colors[i], time);
                    alphaKey[i] = new GradientAlphaKey(colors[i].a, time);
                }

                Gradient g = new Gradient();
                g.SetKeys(colorKey, alphaKey);
                return g;
            }

            //sample colors at regular intervals to create max 8 keys
            //always include first and last point
            GradientColorKey[] colorKeys = new GradientColorKey[maxKeys];
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[maxKeys];

            for (int i = 0; i < maxKeys; i++)
            {
                //calculate position along track
                float time = i / (float)(maxKeys - 1);
                
                //map time to color array index
                int colorIndex = Mathf.RoundToInt(time * (colors.Length - 1));
                colorIndex = Mathf.Clamp(colorIndex, 0, colors.Length - 1);
                
                colorKeys[i] = new GradientColorKey(colors[colorIndex], time);
                alphaKeys[i] = new GradientAlphaKey(colors[colorIndex].a, time);
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        //edge collider setup for physics interactions
        private void SetupEdgeCollider()
        {
            if (edgeCollider == null || smoothedPoints == null)
                return;

            Vector2[] localPoints = new Vector2[smoothedPoints.Length];
            for (int i = 0; i < smoothedPoints.Length; i++)
            {
                localPoints[i] = transform.InverseTransformPoint(smoothedPoints[i]);
            }

            edgeCollider.points = localPoints;
        }

        //vector2 to vector3 array conversion for LineRenderer
        private Vector3[] ConvertToVector3Array(Vector2[] v2Array)
        {
            Vector3[] v3Array = new Vector3[v2Array.Length];
            for (int i = 0; i < v2Array.Length; i++)
            {
                v3Array[i] = new Vector3(v2Array[i].x, v2Array[i].y, 0f);
            }
            return v3Array;
        }

        //track height retrieval at specific X position
        public float GetTrackHeightAt(float x)
        {
            if (smoothedPoints == null || smoothedPoints.Length == 0)
                return float.NaN;

            if (x < smoothedPoints[0].x || x > smoothedPoints[smoothedPoints.Length - 1].x)
                return float.NaN;

            // Find closest point or interpolate between two points
            for (int i = 0; i < smoothedPoints.Length - 1; i++)
            {
                if (x >= smoothedPoints[i].x && x <= smoothedPoints[i + 1].x)
                {
                    // Interpolate between two points
                    float t = (x - smoothedPoints[i].x) / (smoothedPoints[i + 1].x - smoothedPoints[i].x);
                    return Mathf.Lerp(smoothedPoints[i].y, smoothedPoints[i + 1].y, t);
                }
            }

            return float.NaN;
        }

        //track normal retrieval at specific X position
        public Vector2 GetTrackNormalAt(float x)
        {
            if (smoothedPoints == null || smoothedPoints.Length < 2)
                return Vector2.up;

            // Find segment containing X
            for (int i = 0; i < smoothedPoints.Length - 1; i++)
            {
                if (x >= smoothedPoints[i].x && x <= smoothedPoints[i + 1].x)
                {
                    Vector2 direction = (smoothedPoints[i + 1] - smoothedPoints[i]).normalized;
                    // Rotate 90 degrees counter-clockwise to get normal
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    return normal.normalized;
                }
            }

            return Vector2.up;
        }

        //regenerates the track with current settings
        public void RegenerateTrack()
        {
            GenerateTrack();
        }

        //ensure valid parameter values
        private void OnValidate()
        {
            trackLength = Mathf.Max(1f, trackLength);
            heightRange.x = Mathf.Min(heightRange.x, heightRange.y - 0.1f);
            lineWidth = Mathf.Max(0.01f, lineWidth);
        }

        private void OnDrawGizmos()
        {
            if (smoothedPoints != null && smoothedPoints.Length > 1)
            {
                Gizmos.color = Color.green;
                for (int i = 0; i < smoothedPoints.Length - 1; i++)
                {
                    Gizmos.DrawLine(smoothedPoints[i], smoothedPoints[i + 1]);
                }
            }
        }
    }
}

