using UnityEngine;
using UnityEngine.InputSystem;

namespace Solracer.Game
{
    /// <summary>
    /// Debug helper for ATV controller - shows speed, input, and tire rotation.
    /// </summary>
    public class ATVControllerDebug : MonoBehaviour
    {
        [SerializeField] private ATVController atvController;
        [SerializeField] private bool showDebugGUI = true;
        [SerializeField] private bool logInputEvents = false;

        private void Start()
        {
            if (atvController == null)
            {
                atvController = GetComponent<ATVController>();
            }
        }

        private void Update()
        {
            if (atvController == null)
                return;

            // Log input events if enabled
            if (logInputEvents)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.dKey.wasPressedThisFrame)
                    {
                        Debug.Log($"[ATVDebug] Accelerate (D) pressed! Speed: {atvController.CurrentSpeed:F2}");
                    }

                    if (keyboard.aKey.wasPressedThisFrame)
                    {
                        Debug.Log($"[ATVDebug] Brake (A) pressed! Speed: {atvController.CurrentSpeed:F2}");
                    }

                    if (keyboard.spaceKey.wasPressedThisFrame)
                    {
                        Debug.Log($"[ATVDebug] Handbrake (Space) pressed! Stopping tire rotation.");
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!showDebugGUI || atvController == null)
                return;

            // Display debug info on screen
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;

            GUI.Label(new Rect(10, 10, 500, 30), $"Speed: {atvController.CurrentSpeed:F2} m/s", style);
            GUI.Label(new Rect(10, 40, 500, 30), $"Velocity: X={atvController.Velocity.x:F2}, Y={atvController.Velocity.y:F2}", style);
            GUI.Label(new Rect(10, 70, 500, 30), $"Press D to Accelerate (right)", style);
            GUI.Label(new Rect(10, 100, 500, 30), $"Press A to Brake (left)", style);
            GUI.Label(new Rect(10, 130, 500, 30), $"Press Space for Handbrake", style);

            // Show keyboard state
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                GUI.Label(new Rect(10, 160, 500, 30), "⚠️ No keyboard detected!", style);
            }
            else
            {
                GUI.Label(new Rect(10, 160, 500, 30), $"✅ Keyboard: D={keyboard.dKey.isPressed}, A={keyboard.aKey.isPressed}, Space={keyboard.spaceKey.isPressed}", style);
            }

            // Show tire rotation
            var frontTireRb = atvController.FrontTire;
            var backTireRb = atvController.BackTire;
            
            if (frontTireRb != null)
            {
                GUI.Label(new Rect(10, 190, 500, 30), $"Front Tire Angular Velocity: {frontTireRb.angularVelocity:F2} deg/s", style);
            }

            if (backTireRb != null)
            {
                GUI.Label(new Rect(10, 220, 500, 30), $"Back Tire Angular Velocity: {backTireRb.angularVelocity:F2} deg/s", style);
            }
        }
    }
}

