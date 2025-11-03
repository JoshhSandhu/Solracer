using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

namespace Solracer.Game
{
    /// <summary>
    /// ATV controller that applies torque to tires for movement
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class ATVController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Input actions asset")]
        [SerializeField] private InputActionAsset inputActionsAsset;

        [Header("Tire References")]
        [Tooltip("Front tire Rigidbody2D")]
        [SerializeField] private Rigidbody2D frontTire;
        
        [Tooltip("Back tire Rigidbody2D")]
        [SerializeField] private Rigidbody2D backTire;

        [Header("Torque Settings")]
        [Tooltip("Torque force applied to tires when accelerating")]
        [SerializeField] private float accelerationTorque = 500f;
        
        [Tooltip("Torque force applied to tires when braking")]
        [SerializeField] private float brakeTorque = 300f;
        
        [Tooltip("Maximum angular velocity for tires")]
        [SerializeField] private float maxAngularVelocity = 50f;

        [Header("Physics Settings")]
        [Tooltip("Max speed limit for ATV")]
        [SerializeField] private float maxSpeed = 20f;

        [Header("Mid-Air Rotation")]
        [Tooltip("Torque applied to ATV in mid-air for rotation control (like Hill Climb Racing)")]
        [SerializeField] private float midAirTorque = 500f;

        [Tooltip("Check if ATV is grounded before applying mid-air torque")]
        [SerializeField] private bool checkGrounded = true;

        [Tooltip("Ground check distance (raycast distance)")]
        [SerializeField] private float groundCheckDistance = 1f;

        [Tooltip("Layers considered as ground")]
        [SerializeField] private LayerMask groundLayerMask = -1;

        //components
        private Rigidbody2D atvRigidbody;
        private ATVInputActions atvInput;
        private bool isInitialized;

        [Header("Mobile Input")]
        [Tooltip("Mobile input controls component")]
        [SerializeField] private MonoBehaviour mobileInputControls;

        //input state
        private float currentAccelerateInput;
        private float currentBrakeInput;
        private float currentHandbrakeInput;

        //UI input state
        private float uiAccelerateInput = 0f;
        private float uiBrakeInput = 0f;
        private float uiHandbrakeInput = 0f;

        //properties
        //current ATV speed
        public float CurrentSpeed => atvRigidbody != null ? atvRigidbody.linearVelocity.magnitude : 0f;

        //current ATV velocity vector
        public Vector2 Velocity => atvRigidbody != null ? atvRigidbody.linearVelocity : Vector2.zero;

        //front tire Rigidbody2D
        public Rigidbody2D FrontTire => frontTire;

        //back tire Rigidbody2D
        public Rigidbody2D BackTire => backTire;

        private void Awake()
        {
            atvRigidbody = GetComponent<Rigidbody2D>();
            if (atvRigidbody == null)
            {
                Debug.LogError("ATVController: Rigidbody2D not found on ATV!");
                enabled = false;
                return;
            }
            if (frontTire == null || backTire == null)
            {
                FindTires();
            }
            if (frontTire == null || backTire == null)
            {
                Debug.LogError("ATVController: Front or Back tire Rigidbody2D not found!");
                enabled = false;
                return;
            }

            ConfigureRigidbody();
        }

        private void OnEnable()
        {
            if (isInitialized && atvInput != null)
            {
                atvInput.Enable();
            }
        }

        private void Start()
        {
            InitializeInput();
        }

        private void Update()
        {
            if (!isInitialized)
                return;

            //get input from Input System
            float keyboardAccelerate = atvInput.GetAccelerateInput();
            float keyboardBrake = atvInput.GetBrakeInput();
            float keyboardHandbrake = atvInput.GetHandbrakeInput();

            //UI input takes priority
            currentAccelerateInput = uiAccelerateInput > 0f ? uiAccelerateInput : keyboardAccelerate;
            currentBrakeInput = uiBrakeInput > 0f ? uiBrakeInput : keyboardBrake;
            currentHandbrakeInput = uiHandbrakeInput > 0f ? uiHandbrakeInput : keyboardHandbrake;
        }

        /// <summary>
        /// sets accelerate input from UI
        /// </summary>
        public void SetUIAccelerateInput(float value)
        {
            uiAccelerateInput = Mathf.Clamp01(value);
        }

        /// <summary>
        /// sets brake input from UI
        /// </summary>
        public void SetUIBrakeInput(float value)
        {
            uiBrakeInput = Mathf.Clamp01(value);
        }

        /// <summary>
        /// sets handbrake input from UI
        /// </summary>
        public void SetUIHandbrakeInput(float value)
        {
            uiHandbrakeInput = Mathf.Clamp01(value);
        }

        private void FixedUpdate()
        {
            if (!isInitialized || frontTire == null || backTire == null)
                return;

            //handbrake stops tire rotation
            if (currentHandbrakeInput > 0f)
            {
                StopTireRotation();
            }
            else
            {
                //applying torque to tires based on input
                ApplyTireTorque(currentAccelerateInput, currentBrakeInput);
            }

            //mid-air rotation control (like Hill Climb Racing)
            ApplyMidAirRotation();

            //max speed limit
            LimitMaxSpeed();
        }

        /// <summary>
        /// Stops tire rotation (handbrake).
        /// Sets tire angular velocity to 0 to lock the tires.
        /// </summary>
        private void StopTireRotation()
        {
            if (frontTire != null)
            {
                frontTire.angularVelocity = 0f;
            }

            if (backTire != null)
            {
                backTire.angularVelocity = 0f;
            }
        }

        /// <summary>
        /// finds front and back tires by searching child objects
        /// </summary>
        private void FindTires()
        {
            Transform[] children = GetComponentsInChildren<Transform>();
            
            foreach (Transform child in children)
            {
                if (child.name.Contains("Front_tire") || child.name.Contains("Front"))
                {
                    frontTire = child.GetComponent<Rigidbody2D>();
                    if (frontTire != null)
                    {
                        Debug.Log($"ATVController: Found Front tire - {child.name}");
                    }
                }
                else if (child.name.Contains("Back_tire") || child.name.Contains("Back"))
                {
                    backTire = child.GetComponent<Rigidbody2D>();
                    if (backTire != null)
                    {
                        Debug.Log($"ATVController: Found Back tire - {child.name}");
                    }
                }
            }
        }

        /// <summary>
        /// ATV Rigidbody2D settings.
        /// </summary>
        private void ConfigureRigidbody()
        {
            if (atvRigidbody == null)
                return;

            atvRigidbody.bodyType = RigidbodyType2D.Dynamic;
            atvRigidbody.simulated = true;

            //config of the physics properties
            atvRigidbody.linearDamping = 0.1f;  //slight air resistance
            atvRigidbody.angularDamping = 2f;   //prevent excessive rotation

            atvRigidbody.WakeUp();

            Debug.Log("ATVController: Rigidbody2D configured");
        }

        /// <summary>
        /// init input system
        /// </summary>
        private void InitializeInput()
        {
            if (inputActionsAsset == null)
            {
                //finding bike controler asset automatically
                inputActionsAsset = UnityEngine.Resources.FindObjectsOfTypeAll<InputActionAsset>()
                    .FirstOrDefault(asset => asset.name == "BikeControler");

                if (inputActionsAsset == null)
                {
                    Debug.LogError("ATVController: Input Actions asset not found!");
                    enabled = false;
                    return;
                }

                Debug.Log($"ATVController: Found BikeControler asset");
            }

            atvInput = new ATVInputActions();
            bool success = atvInput.Initialize(inputActionsAsset);

            if (!success)
            {
                Debug.LogError("ATVController: Failed to initialize input system!");
                enabled = false;
                return;
            }

            isInitialized = true;
            Debug.Log("ATVController: Successfully initialized!");
        }

        /// <summary>
        /// torque to both tires based on input
        /// </summary>
        private void ApplyTireTorque(float accelerate, float brake)
        {
            float netTorque = 0f;

            if (accelerate > 0f)
            {
                netTorque = -accelerationTorque * accelerate;
            }
            else if (brake > 0f)
            {
                netTorque = brakeTorque * brake;
            }

            if (netTorque != 0f)
            {
                if (frontTire != null)
                {
                    frontTire.AddTorque(netTorque, ForceMode2D.Force);

                    //limit angular velocity to prevent unrealistic spinning
                    if (Mathf.Abs(frontTire.angularVelocity) > maxAngularVelocity)
                    {
                        frontTire.angularVelocity = Mathf.Sign(frontTire.angularVelocity) * maxAngularVelocity;
                    }
                }

                if (backTire != null)
                {
                    backTire.AddTorque(netTorque, ForceMode2D.Force);
               
                    if (Mathf.Abs(backTire.angularVelocity) > maxAngularVelocity)
                    {
                        backTire.angularVelocity = Mathf.Sign(backTire.angularVelocity) * maxAngularVelocity;
                    }
                }
            }
        }

        /// <summary>
        /// mid-air rotation torque based on accelerate/brake input
        /// </summary>
        private void ApplyMidAirRotation()
        {
            if (atvRigidbody == null || midAirTorque <= 0f)
                return;

            //check if grounded
            bool isGrounded = true;
            if (checkGrounded)
            {
                isGrounded = IsGrounded();
            }

            if (!checkGrounded || !isGrounded)
            {
                float rotationInput = 0f;

                //accelerate input rotates forward
                if (currentAccelerateInput > 0f)
                {
                    rotationInput -= currentAccelerateInput;
                }

                //brake/reverse input rotates backward
                if (currentBrakeInput > 0f)
                {
                    rotationInput += currentBrakeInput;
                }

                if (Mathf.Abs(rotationInput) > 0.01f)
                {
                    float torque = rotationInput * midAirTorque;
                    atvRigidbody.AddTorque(torque, ForceMode2D.Force);
                }
            }
        }

        /// <summary>
        /// Checks if ATV is grounded using raycast
        /// </summary>
        private bool IsGrounded()
        {
            if (atvRigidbody == null)
                return false;

            //cast ray downward from ATV position
            Vector2 position = transform.position;
            Vector2 direction = Vector2.down;

            RaycastHit2D hit = Physics2D.Raycast(position, direction, groundCheckDistance, groundLayerMask);

            bool frontTireGrounded = false;
            bool backTireGrounded = false;

            if (frontTire != null)
            {
                RaycastHit2D frontHit = Physics2D.Raycast(frontTire.transform.position, direction, groundCheckDistance, groundLayerMask);
                frontTireGrounded = frontHit.collider != null;
            }

            if (backTire != null)
            {
                RaycastHit2D backHit = Physics2D.Raycast(backTire.transform.position, direction, groundCheckDistance, groundLayerMask);
                backTireGrounded = backHit.collider != null;
            }

            //grounded if raycast hits ground
            return hit.collider != null || frontTireGrounded || backTireGrounded;
        }

        /// <summary>
        /// ATV max speed
        /// </summary>
        private void LimitMaxSpeed()
        {
            if (atvRigidbody == null)
                return;

            Vector2 velocity = atvRigidbody.linearVelocity;
            if (velocity.magnitude > maxSpeed)
            {
                atvRigidbody.linearVelocity = velocity.normalized * maxSpeed;
            }
        }

        /// <summary>
        /// ATV to start position
        /// </summary>
        public void ResetATV(Vector2 position)
        {
            if (atvRigidbody == null)
                return;

            transform.position = position;
            transform.rotation = Quaternion.identity;
         
            atvRigidbody.linearVelocity = Vector2.zero;
            atvRigidbody.angularVelocity = 0f;

            if (frontTire != null)
            {
                frontTire.linearVelocity = Vector2.zero;
                frontTire.angularVelocity = 0f;
            }

            if (backTire != null)
            {
                backTire.linearVelocity = Vector2.zero;
                backTire.angularVelocity = 0f;
            }

            atvRigidbody.WakeUp();
            frontTire?.WakeUp();
            backTire?.WakeUp();
        }

        
        //enable input
        public void EnableInput()
        {
            atvInput?.Enable();
        }

        //Disable input
        public void DisableInput()
        {
            atvInput?.Disable();
        }

        //cleanup
        private void OnDestroy()
        {
            atvInput?.Dispose();
        }
    }
}

