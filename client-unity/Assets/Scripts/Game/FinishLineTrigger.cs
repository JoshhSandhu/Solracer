using UnityEngine;

namespace Solracer.Game
{
    /// <summary>
    /// Trigger component for finish line
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class FinishLineTrigger : MonoBehaviour
    {
        [Tooltip("Race Manager to notify when finish line is crossed")]
        [SerializeField] private RaceManager raceManager;

        [Tooltip("Only trigger once")]
        [SerializeField] private bool triggerOnce = true;

        private bool hasTriggered = false;

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning("FinishLineTrigger: Collider was not set as trigger. Setting it now.");
            }
        }

        private void Start()
        {
            if (raceManager == null)
            {
                raceManager = FindAnyObjectByType<RaceManager>();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.name.Contains("ATV") || other.CompareTag("Player"))
            {
                if (!hasTriggered || !triggerOnce)
                {
                    hasTriggered = true;
                    
                    if (raceManager != null)
                    {
                        raceManager.OnFinishLineCrossed();
                    }
                    else
                    {
                        Debug.LogWarning("FinishLineTrigger: RaceManager is null! Cannot trigger race complete.");
                    }
                }
            }
        }

        /// <summary>
        /// RaceManager reference
        /// </summary>
        public void SetRaceManager(RaceManager manager)
        {
            raceManager = manager;
        }
    }
}

