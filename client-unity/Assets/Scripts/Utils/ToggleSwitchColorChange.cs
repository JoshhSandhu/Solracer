using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Toggle.UI
{
    public class ToggleSwitchColorChange : ToggleSwitch
    {
        [Header("Elements to Recolor")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image handleImage;
        [Space]
        [SerializeField] private bool recolorBackground;
        [SerializeField] private bool recolorHandle;
        [SerializeField] private bool TransitionText;
        [Header("Colors")]
        [SerializeField] private Color backgroundColorOff = Color.white;
        [SerializeField] private Color backgroundColorOn = Color.white;
        [Space]
        [SerializeField] private Color handleColorOff = Color.white;
        [SerializeField] private Color handleColorOn = Color.white;
        [Header("Text")]
        [SerializeField] private TextMeshProUGUI text1;
        [SerializeField] private TextMeshProUGUI text2;

        private bool _isBackgroundImageNotNull;
        private bool _isHandleImageNotNull;
        private bool _isTransitionText1NotNull;
        private bool _isTransitionText2NotNull;

        protected override void OnValidate()
        {
            base.OnValidate();
            CheckForNull();
            ChangeColors();
        }

        private void OnEnable()
        {
            transitionEffect += ChangeColors;
        }

        private void OnDisable()
        {
            transitionEffect -= ChangeColors;
        }

        protected override void Awake()
        {
            base.Awake();
            CheckForNull();
            ChangeColors();
        }

        private void CheckForNull()
        {
            _isBackgroundImageNotNull = backgroundImage != null;
            _isHandleImageNotNull = handleImage != null;
            _isTransitionText1NotNull = text1 != null;
            _isTransitionText2NotNull = text2 != null;
        }

        private void ChangeColors()
        {
            if (recolorBackground && _isBackgroundImageNotNull)
                backgroundImage.color = Color.Lerp(backgroundColorOff, backgroundColorOn, sliderValue);

            if (recolorHandle && _isHandleImageNotNull)
                handleImage.color = Color.Lerp(handleColorOff, handleColorOn, sliderValue);
            ChangeText();
        }

        private void ChangeText()
        {
            if (!TransitionText) return;

            if (_isTransitionText1NotNull)
            {
                Color text1Color = text1.color;
                text1Color.a = 1f - sliderValue;
                text1.color = text1Color;
            }

            if (_isTransitionText2NotNull)
            {
                Color text2Color = text2.color;
                text2Color.a = sliderValue;
                text2.color = text2Color;
            }
        }
    }
}