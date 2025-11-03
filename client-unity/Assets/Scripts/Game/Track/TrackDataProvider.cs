using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game
{
    /// <summary>
    /// mock data provider for track generation
    /// returns 1000 normalized points for track height data
    /// </summary>
    public static class TrackDataProvider
    {
        private const int PointCount = 1000;
        private static float[] cachedPoints = null;

        /// <summary>
        /// get mock track data as normalized points
        /// return 1000 points representing height variation along the track
        /// </summary>
        public static float[] GetMockTrackData()
        {
            //cache points for performance
            if (cachedPoints != null)
            {
                return cachedPoints;
            }
            cachedPoints = new float[PointCount];

            //smooth track using combined sine waves with low frequency for rolling hills (Hill Climb Racing style)
            for (int i = 0; i < PointCount; i++)
            {
                float t = (float)i / PointCount; //0-1 range
                float value = 0f;                //multiple sine waves combined

                //Lower frequency waves for smoother, more spread out hills
                value += Mathf.Sin(t * Mathf.PI * 0.5f) * 0.5f;  //very long wave (2 complete cycles over track)
                value += Mathf.Sin(t * Mathf.PI * 1f) * 0.3f;     //long wave (1 complete cycle)
                value += Mathf.Sin(t * Mathf.PI * 2f) * 0.15f;    //medium wave (2 cycles)
                //Reduced noise for smoother terrain
                value += Random.Range(-0.05f, 0.05f);            //minimal random noise
                value = (value + 1f) * 0.5f;                     //normalize to 0-1
                value = Mathf.Clamp01(value);                     //clamp to valid range
                cachedPoints[i] = value;
            }
            return cachedPoints;
        }

        /// <summary>
        /// track data with a specific seed for deterministic generation.
        /// </summary>
        public static float[] GetMockTrackDataWithSeed(int seed)
        {
            Random.State oldState = Random.state;   //save current random state
            Random.InitState(seed);                 //initialize with seed
            float[] points = new float[PointCount]; //array for points

            for (int i = 0; i < PointCount; i++)
            {
                float t = (float)i / PointCount;
          
                float value = 0f;
                //Lower frequency waves for smoother, more spread out hills
                value += Mathf.Sin(t * Mathf.PI * 0.5f) * 0.5f;  //very long wave (2 complete cycles over track)
                value += Mathf.Sin(t * Mathf.PI * 1f) * 0.3f;     //long wave (1 complete cycle)
                value += Mathf.Sin(t * Mathf.PI * 2f) * 0.15f;    //medium wave (2 cycles)
                //Reduced noise for smoother terrain
                value += Random.Range(-0.05f, 0.05f);            //minimal random noise
                value = (value + 1f) * 0.5f;
                value = Mathf.Clamp01(value);
                points[i] = value;
            }
            
            Random.state = oldState; //restore previous random state
            return points;
        }

        //reset cached points
        public static void ResetCache()
        {
            cachedPoints = null;
        }

        //get number of points in track data
        public static int GetPointCount()
        {
            return PointCount;
        }
    }
}

