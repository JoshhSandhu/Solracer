using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Solracer.Game;

namespace Solracer.UI
{
    /// <summary>
    /// mobile UI button controls for ATV
    /// </summary>
    public class MobileInputControls : MonoBehaviour
    {
        [Header("ATV Controller Reference")]
        [Tooltip("ATV Controller to send input to")]
        [SerializeField] private ATVController atvController;

        [Header("UI Buttons")]
        [Tooltip("Accelerate button")]
        [SerializeField] private Button accelerateButton;

        [Tooltip("Reverse button")]
        [SerializeField] private Button reverseButton;

        [Tooltip("Handbrake button")]
        [SerializeField] private Button handbrakeButton;

        //input state
        private bool isAccelerating = false;
        private bool isReversing = false;
        private bool isHandbraking = false;

        private void Awake()
        {
            if (atvController == null)
            {
                GameObject atvObject = GameObject.Find("ATV");
                if (atvObject != null)
                {
                    atvController = atvObject.GetComponent<ATVController>();
                }
            }
        }

        private void Start()
        {
            SetupButtons();
        }

        private void Update()
        {
            if (atvController != null)
            {
                atvController.SetUIAccelerateInput(isAccelerating ? 1f : 0f);
                atvController.SetUIBrakeInput(isReversing ? 1f : 0f);
                atvController.SetUIHandbrakeInput(isHandbraking ? 1f : 0f);
            }
        }

        private void OnDisable()
        {
            ReleaseAccelerate();
            ReleaseReverse();
            ReleaseHandbrake();
        }

        /// <summary>
        /// sets up button event listeners
        /// </summary>
        private void SetupButtons()
        {
            //accelerate button
            if (accelerateButton != null)
            {
                //eventTrigger for press/hold/release
                EventTrigger trigger = accelerateButton.gameObject.GetComponent<EventTrigger>();
                if (trigger == null)
                {
                    trigger = accelerateButton.gameObject.AddComponent<EventTrigger>();
                }

                //Clear existing triggers
                trigger.triggers.Clear();

                EventTrigger.Entry pointerDown = new EventTrigger.Entry();
                pointerDown.eventID = EventTriggerType.PointerDown;
                pointerDown.callback.AddListener((data) => { PressAccelerate(); });
                trigger.triggers.Add(pointerDown);

                EventTrigger.Entry pointerUp = new EventTrigger.Entry();
                pointerUp.eventID = EventTriggerType.PointerUp;
                pointerUp.callback.AddListener((data) => { ReleaseAccelerate(); });
                trigger.triggers.Add(pointerUp);

                EventTrigger.Entry pointerExit = new EventTrigger.Entry();
                pointerExit.eventID = EventTriggerType.PointerExit;
                pointerExit.callback.AddListener((data) => { ReleaseAccelerate(); });
                trigger.triggers.Add(pointerExit);
            }

            //reverse button
            if (reverseButton != null)
            {
                EventTrigger trigger = reverseButton.gameObject.GetComponent<EventTrigger>();
                if (trigger == null)
                {
                    trigger = reverseButton.gameObject.AddComponent<EventTrigger>();
                }

                trigger.triggers.Clear();

                EventTrigger.Entry pointerDown = new EventTrigger.Entry();
                pointerDown.eventID = EventTriggerType.PointerDown;
                pointerDown.callback.AddListener((data) => { PressReverse(); });
                trigger.triggers.Add(pointerDown);

                EventTrigger.Entry pointerUp = new EventTrigger.Entry();
                pointerUp.eventID = EventTriggerType.PointerUp;
                pointerUp.callback.AddListener((data) => { ReleaseReverse(); });
                trigger.triggers.Add(pointerUp);

                EventTrigger.Entry pointerExit = new EventTrigger.Entry();
                pointerExit.eventID = EventTriggerType.PointerExit;
                pointerExit.callback.AddListener((data) => { ReleaseReverse(); });
                trigger.triggers.Add(pointerExit);
            }

            //handbrake button
            if (handbrakeButton != null)
            {
                EventTrigger trigger = handbrakeButton.gameObject.GetComponent<EventTrigger>();
                if (trigger == null)
                {
                    trigger = handbrakeButton.gameObject.AddComponent<EventTrigger>();
                }

                trigger.triggers.Clear();

                EventTrigger.Entry pointerDown = new EventTrigger.Entry();
                pointerDown.eventID = EventTriggerType.PointerDown;
                pointerDown.callback.AddListener((data) => { PressHandbrake(); });
                trigger.triggers.Add(pointerDown);

                EventTrigger.Entry pointerUp = new EventTrigger.Entry();
                pointerUp.eventID = EventTriggerType.PointerUp;
                pointerUp.callback.AddListener((data) => { ReleaseHandbrake(); });
                trigger.triggers.Add(pointerUp);

                EventTrigger.Entry pointerExit = new EventTrigger.Entry();
                pointerExit.eventID = EventTriggerType.PointerExit;
                pointerExit.callback.AddListener((data) => { ReleaseHandbrake(); });
                trigger.triggers.Add(pointerExit);
            }
        }

        /// <summary>
        /// Accelerate button is pressed
        /// </summary>
        public void PressAccelerate()
        {
            isAccelerating = true;
        }

        /// <summary>
        /// Accelerate button is released
        /// </summary>
        public void ReleaseAccelerate()
        {
            isAccelerating = false;
        }

        /// <summary>
        /// Reverse button is pressed
        /// </summary>
        public void PressReverse()
        {
            isReversing = true;
        }

        /// <summary>
        /// Reverse button is released
        /// </summary>
        public void ReleaseReverse()
        {
            isReversing = false;
        }

        /// <summary>
        /// Handbrake button is pressed
        /// </summary>
        public void PressHandbrake()
        {
            isHandbraking = true;
        }

        /// <summary>
        /// Handbrake button is released
        /// </summary>
        public void ReleaseHandbrake()
        {
            isHandbraking = false;
        }

        public void SetATVController(ATVController controller)
        {
            atvController = controller;
        }

        //getting input states
        public bool IsAccelerating => isAccelerating;
        public bool IsReversing => isReversing;
        public bool IsHandbraking => isHandbraking;
    }
}

