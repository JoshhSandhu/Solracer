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
        private RaceManager raceManager;
        private Animator accelerateAnimator;
        private Animator reverseAnimator;
        private Animator handbrakeAnimator;
        private bool lastControlsEnabled = true;

        private static readonly int IsHeldHash = Animator.StringToHash("IsHeld");
        private static readonly int IsDisabledHash = Animator.StringToHash("IsDisabled");
        private static readonly int PressHash = Animator.StringToHash("Press");
        private static readonly int ErrorHash = Animator.StringToHash("Error");

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

            raceManager = FindAnyObjectByType<RaceManager>();
            CacheAnimators();
        }

        private void Start()
        {
            SetupButtons();
        }

        private void Update()
        {
            bool controlsEnabled = AreControlsEnabled();
            UpdateAnimatorDisabledStates(controlsEnabled);

            if (atvController != null)
            {
                atvController.SetUIAccelerateInput(controlsEnabled && isAccelerating ? 1f : 0f);
                atvController.SetUIBrakeInput(controlsEnabled && isReversing ? 1f : 0f);
                atvController.SetUIHandbrakeInput(controlsEnabled && isHandbraking ? 1f : 0f);
            }
        }

        private void OnDisable()
        {
            ReleaseAccelerate();
            ReleaseReverse();
            ReleaseHandbrake();
        }

        private void CacheAnimators()
        {
            accelerateAnimator = accelerateButton != null ? accelerateButton.GetComponent<Animator>() : null;
            reverseAnimator = reverseButton != null ? reverseButton.GetComponent<Animator>() : null;
            handbrakeAnimator = handbrakeButton != null ? handbrakeButton.GetComponent<Animator>() : null;
        }

        private bool AreControlsEnabled()
        {
            if (raceManager == null)
            {
                return true;
            }

            return raceManager.IsGameActive && !raceManager.HasReachedEnd;
        }

        private void UpdateAnimatorDisabledStates(bool controlsEnabled)
        {
            if (lastControlsEnabled == controlsEnabled)
            {
                return;
            }

            lastControlsEnabled = controlsEnabled;

            SetAnimatorDisabledState(accelerateAnimator, !controlsEnabled);
            SetAnimatorDisabledState(reverseAnimator, !controlsEnabled);
            SetAnimatorDisabledState(handbrakeAnimator, !controlsEnabled);
        }

        private void SetAnimatorDisabledState(Animator animator, bool isDisabled)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetBool(IsDisabledHash, isDisabled);
        }

        private void PlayPressAnimation(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(PressHash);
            animator.SetBool(IsHeldHash, true);
        }

        private void ReleaseHeldAnimation(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetBool(IsHeldHash, false);
        }

        private void PlayErrorAnimation(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(ErrorHash);
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
            if (!AreControlsEnabled())
            {
                PlayErrorAnimation(accelerateAnimator);
                return;
            }

            isAccelerating = true;
            PlayPressAnimation(accelerateAnimator);
        }

        /// <summary>
        /// Accelerate button is released
        /// </summary>
        public void ReleaseAccelerate()
        {
            isAccelerating = false;
            ReleaseHeldAnimation(accelerateAnimator);
        }

        /// <summary>
        /// Reverse button is pressed
        /// </summary>
        public void PressReverse()
        {
            if (!AreControlsEnabled())
            {
                PlayErrorAnimation(reverseAnimator);
                return;
            }

            isReversing = true;
            PlayPressAnimation(reverseAnimator);
        }

        /// <summary>
        /// Reverse button is released
        /// </summary>
        public void ReleaseReverse()
        {
            isReversing = false;
            ReleaseHeldAnimation(reverseAnimator);
        }

        /// <summary>
        /// Handbrake button is pressed
        /// </summary>
        public void PressHandbrake()
        {
            if (!AreControlsEnabled())
            {
                PlayErrorAnimation(handbrakeAnimator);
                return;
            }

            isHandbraking = true;
            PlayPressAnimation(handbrakeAnimator);
        }

        /// <summary>
        /// Handbrake button is released
        /// </summary>
        public void ReleaseHandbrake()
        {
            isHandbraking = false;
            ReleaseHeldAnimation(handbrakeAnimator);
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
