using UnityEngine;
using UnityEngine.InputSystem;

namespace TAD.Progression
{
    /// <summary>
    /// Gives a short, platform-safe tactile warning when an animal reaches the food barn.
    /// Phones use their native vibration; connected gamepads use a brief dual-motor rumble.
    /// </summary>
    [DefaultExecutionOrder(-890)]
    [DisallowMultipleComponent]
    public sealed class TadHapticFeedback : MonoBehaviour
    {
        private const string FeedbackObjectName = "TAD Haptic Feedback";
        private const float FoodStoreLowMotor = 0.2f;
        private const float FoodStoreHighMotor = 0.5f;
        private const float FoodStorePulseDuration = 0.16f;

        private static TadHapticFeedback instance;

        private Gamepad activeGamepad;
        private float stopGamepadAt = -1f;

        /// <summary>Plays the food-store-hit vibration once for a confirmed animal arrival.</summary>
        public static void PulseFoodStoreReached()
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif

            if (Gamepad.current == null)
            {
                return;
            }

            EnsureInstance().PulseGamepad(Gamepad.current);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (activeGamepad != null && Time.unscaledTime >= stopGamepadAt)
            {
                StopGamepadRumble();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                StopGamepadRumble();
            }
        }

        private void OnDestroy()
        {
            StopGamepadRumble();
            if (instance == this)
            {
                instance = null;
            }
        }

        private void PulseGamepad(Gamepad gamepad)
        {
            if (gamepad == null)
            {
                return;
            }

            activeGamepad = gamepad;
            activeGamepad.SetMotorSpeeds(FoodStoreLowMotor, FoodStoreHighMotor);
            stopGamepadAt = Time.unscaledTime + FoodStorePulseDuration;
        }

        private void StopGamepadRumble()
        {
            if (activeGamepad != null)
            {
                activeGamepad.SetMotorSpeeds(0f, 0f);
            }

            activeGamepad = null;
            stopGamepadAt = -1f;
        }

        private static TadHapticFeedback EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<TadHapticFeedback>(FindObjectsInactive.Include);
            if (instance != null)
            {
                return instance;
            }

            var feedbackObject = new GameObject(FeedbackObjectName);
            instance = feedbackObject.AddComponent<TadHapticFeedback>();
            return instance;
        }
    }
}
