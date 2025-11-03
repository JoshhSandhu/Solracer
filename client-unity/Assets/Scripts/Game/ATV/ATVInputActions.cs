using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

namespace Solracer.Game
{
    /// <summary>
    /// Wrapper for BikeControler input actions
    /// </summary>
    public class ATVInputActions
    {
        private InputActionAsset inputActionsAsset;
        private InputActionMap bikeControlerMap;
        private InputAction accelerateAction;
        private InputAction brakeAction;
        private InputAction handbrakeAction;
        private bool isEnabled;

        /// <summary>
        /// initializes the input system
        /// </summary>
        public bool Initialize(InputActionAsset inputActionsAsset)
        {
            if (inputActionsAsset == null)
            {
                Debug.LogError("ATVInputActions: Input actions asset is null!");
                return false;
            }

            this.inputActionsAsset = inputActionsAsset;

            bikeControlerMap = inputActionsAsset.FindActionMap("BikeControler");
            if (bikeControlerMap == null)
            {
                Debug.LogError("ATVInputActions: BikeControler action map not found!");
                Debug.LogError($"Available action maps: {string.Join(", ", inputActionsAsset.actionMaps.Select(m => m.name))}");
                return false;
            }

            accelerateAction = bikeControlerMap.FindAction("Accelerate");
            brakeAction = bikeControlerMap.FindAction("Brake");
            handbrakeAction = bikeControlerMap.FindAction("handbrake");

            if (accelerateAction == null || brakeAction == null)
            {
                Debug.LogError("ATVInputActions: Accelerate or Brake actions not found!");
                if (bikeControlerMap != null)
                {
                    Debug.LogError($"Available actions in BikeControler: {string.Join(", ", bikeControlerMap.actions.Select(a => a.name))}");
                }
                return false;
            }

            if (handbrakeAction == null)
            {
                Debug.LogWarning("ATVInputActions: Handbrake action not found! Handbrake will not work.");
            }

            Enable();
            Debug.Log("ATVInputActions: Successfully initialized!");
            return true;
        }

        /// <summary>
        /// accelerate input value
        /// </summary>
        public float GetAccelerateInput()
        {
            if (!isEnabled || accelerateAction == null)
                return 0f;

            //check if pressed first
            if (accelerateAction.IsPressed())
            {
                return 1f;
            }

            float value = accelerateAction.ReadValue<float>();
            
            if (value == 0f && accelerateAction.triggered)
            {
                return 1f;
            }

            return Mathf.Clamp01(value);
        }

        /// <summary>
        /// brake input value
        /// </summary>
        public float GetBrakeInput()
        {
            if (!isEnabled || brakeAction == null)
                return 0f;

            if (brakeAction.IsPressed())
            {
                return 1f;
            }

            float value = brakeAction.ReadValue<float>();
            
            if (value == 0f && brakeAction.triggered)
            {
                return 1f;
            }

            return Mathf.Clamp01(value);
        }

        /// <summary>
        /// handbrake input value
        /// </summary>
        public float GetHandbrakeInput()
        {
            if (!isEnabled || handbrakeAction == null)
                return 0f;

            //check if pressed first
            if (handbrakeAction.IsPressed())
            {
                return 1f;
            }

            float value = handbrakeAction.ReadValue<float>();
            
            if (value == 0f && handbrakeAction.triggered)
            {
                return 1f;
            }

            return Mathf.Clamp01(value);
        }

        //enable input actions
        public void Enable()
        {
            if (bikeControlerMap != null && !isEnabled)
            {
                bikeControlerMap.Enable();
                isEnabled = true;
            }
        }

        ///disable input actions
        public void Disable()
        {
            if (bikeControlerMap != null && isEnabled)
            {
                bikeControlerMap.Disable();
                isEnabled = false;
            }
        }

        //cleanup
        public void Dispose()
        {
            Disable();
            accelerateAction?.Dispose();
            brakeAction?.Dispose();
            handbrakeAction?.Dispose();
        }
    }
}

