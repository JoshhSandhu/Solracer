using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;

namespace Solracer.Game
{
    /// <summary>
    /// Records player input trace for replay verification
    /// Captures inputs at fixed intervals for deterministic replay
    /// </summary>
    public class InputTraceRecorder : MonoBehaviour
    {
        [Header("Recording Settings")]
        [Tooltip("Fixed timestep for recording (should match Unity's Fixed Timestep)")]
        [SerializeField] private float recordingInterval = 0.0167f; //60 FPS

        [Tooltip("Input actions asset")]
        [SerializeField] private InputActionAsset inputActionsAsset;

        private List<InputFrame> inputTrace = new List<InputFrame>();
        private float lastRecordTime = 0f;
        private bool isRecording = false;
        private InputAction accelerateAction;
        private InputAction brakeAction;
        private InputAction rotateAction;

        //input frame data structure
        [Serializable]
        public class InputFrame
        {
            public float time;
            public float accelerate;
            public float brake;
            public float rotate;
        }


        //get the recorded input trace.
        public List<InputFrame> GetInputTrace()
        {
            return new List<InputFrame>(inputTrace);
        }

        //get input trace as JSON string.
        public string GetInputTraceJson()
        {
            return JsonConvert.SerializeObject(inputTrace, Formatting.None);
        }

        //calculate SHA256 hash of input trace for verification.
        public string CalculateInputHash()
        {
            string json = GetInputTraceJson();
            return CalculateSHA256(json);
        }

        private string CalculateSHA256(string input)
        {
            using (System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        //start recording input trace.
        public void StartRecording()
        {
            if (isRecording)
            {
                Debug.LogWarning("[InputTraceRecorder] Already recording");
                return;
            }

            inputTrace.Clear();
            lastRecordTime = 0f;
            isRecording = true;

            if (inputActionsAsset != null)
            {
                var map = inputActionsAsset.FindActionMap("ATV");
                if (map != null)
                {
                    accelerateAction = map.FindAction("Accelerate");
                    brakeAction = map.FindAction("Brake");
                    rotateAction = map.FindAction("Rotate");
                }
            }

            Debug.Log("[InputTraceRecorder] Started recording input trace");
        }

        //stop recording input trace.
        public void StopRecording()
        {
            if (!isRecording)
            {
                return;
            }

            isRecording = false;
            Debug.Log($"[InputTraceRecorder] Stopped recording. Captured {inputTrace.Count} frames");
        }

        //clear recorded input trace.
        public void ClearTrace()
        {
            inputTrace.Clear();
            lastRecordTime = 0f;
        }

        private void FixedUpdate()
        {
            if (!isRecording)
            {
                return;
            }

            if (Time.fixedTime - lastRecordTime >= recordingInterval) //record at fixed intervals
            {
                RecordInputFrame();
                lastRecordTime = Time.fixedTime;
            }
        }

        private void RecordInputFrame()
        {
            float accelerate = 0f;
            float brake = 0f;
            float rotate = 0f;

            if (accelerateAction != null && accelerateAction.enabled)
            {
                accelerate = accelerateAction.ReadValue<float>();
            }

            if (brakeAction != null && brakeAction.enabled)
            {
                brake = brakeAction.ReadValue<float>();
            }

            if (rotateAction != null && rotateAction.enabled)
            {
                rotate = rotateAction.ReadValue<float>();
            }

            InputFrame frame = new InputFrame  //create input frame
            {
                time = Time.fixedTime,
                accelerate = accelerate,
                brake = brake,
                rotate = rotate
            };

            inputTrace.Add(frame);
        }

        private void OnDestroy()
        {
            StopRecording();
        }
    }
}

