using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CkQol.UI
{
    /// Small helpers for building uGUI by hand. There is no prefab to instantiate -
    /// PugMod script mods ship source only, so every widget is constructed in code.
    public static class UiFactory
    {
        public static GameObject Node(string name, Transform parent, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return go;
        }

        public static Image Panel(string name, Transform parent, Color color, out RectTransform rect)
        {
            var go = Node(name, parent, out rect);
            var image = go.AddComponent<Image>();
            image.color = color;

            var sprite = GameTheme.PanelSprite;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, string content,
                                            float size, Color color,
                                            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = Node(name, parent, out RectTransform rect);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            GameTheme.ApplyText(text, size, color, align);
            return text;
        }

        public static Button FlatButton(string name, Transform parent, string content,
                                        float height, Action onClick)
        {
            var go = Node(name, parent, out RectTransform rect);
            var image = go.AddComponent<Image>();
            image.color = GameTheme.TabIdle;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;

            var label = Label("Text", go.transform, content, 15f, GameTheme.Text,
                              TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 8f, 0f);
            return button;
        }

        /// A row: fixed-width label on the left, caller's widget on the right.
        public static GameObject Row(Transform parent, string label, string tooltip,
                                     float height, out RectTransform slot)
        {
            var go = Node("Row", parent, out RectTransform rect);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;

            var text = Label("Label", go.transform, label, 14f, GameTheme.Text);
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(0.45f, 1f);
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);
            if (!string.IsNullOrEmpty(tooltip)) text.text = label;

            Node("Slot", go.transform, out slot);
            slot.anchorMin = new Vector2(0.45f, 0f);
            slot.anchorMax = new Vector2(1f, 1f);
            slot.offsetMin = new Vector2(4f, 4f);
            slot.offsetMax = new Vector2(-4f, -4f);
            return go;
        }

        public static Toggle Checkbox(Transform parent, bool value, Action<bool> onChanged)
        {
            var go = Node("Toggle", parent, out RectTransform rect);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(22f, 22f);

            var bg = go.AddComponent<Image>();
            bg.color = GameTheme.SliderTrack;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bg;

            var checkGo = Node("Check", go.transform, out RectTransform checkRect);
            Stretch(checkRect, 4f, 4f);
            var check = checkGo.AddComponent<Image>();
            check.color = GameTheme.Accent;
            toggle.graphic = check;

            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        public static Slider HorizontalSlider(Transform parent, float value, float min, float max,
                                              bool wholeNumbers, Action<float> onChanged,
                                              out TextMeshProUGUI readout)
        {
            var go = Node("Slider", parent, out RectTransform rect);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, -10f);
            rect.offsetMax = new Vector2(-62f, 10f);

            var bgGo = Node("Track", go.transform, out RectTransform bgRect);
            Stretch(bgRect, 0f, 6f);
            var bgImage = bgGo.AddComponent<Image>();
            bgImage.color = GameTheme.SliderTrack;

            var fillArea = Node("FillArea", go.transform, out RectTransform fillAreaRect);
            Stretch(fillAreaRect, 0f, 6f);
            var fillGo = Node("Fill", fillArea.transform, out RectTransform fillRect);
            Stretch(fillRect, 0f, 0f);
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = GameTheme.Accent;

            var handleArea = Node("HandleArea", go.transform, out RectTransform handleAreaRect);
            Stretch(handleAreaRect, 0f, 0f);
            var handleGo = Node("Handle", handleArea.transform, out RectTransform handleRect);
            handleRect.sizeDelta = new Vector2(12f, 20f);
            var handleImage = handleGo.AddComponent<Image>();
            handleImage.color = GameTheme.Text;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = value;

            readout = Label("Readout", go.transform,
                            wholeNumbers ? value.ToString("0") : value.ToString("0.00"),
                            13f, GameTheme.TextDim, TextAlignmentOptions.MidlineRight);
            var readoutRect = readout.rectTransform;
            readoutRect.anchorMin = new Vector2(1f, 0.5f);
            readoutRect.anchorMax = new Vector2(1f, 0.5f);
            readoutRect.pivot = new Vector2(0f, 0.5f);
            readoutRect.sizeDelta = new Vector2(58f, 20f);
            readoutRect.anchoredPosition = new Vector2(4f, 0f);

            var captured = readout;
            slider.onValueChanged.AddListener(v =>
            {
                captured.text = wholeNumbers ? v.ToString("0") : v.ToString("0.00");
                onChanged(v);
            });
            return slider;
        }

        public static TMP_InputField TextBox(Transform parent, string value, Action<string> onChanged)
        {
            var go = Node("Input", parent, out RectTransform rect);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, -12f);
            rect.offsetMax = new Vector2(0f, 12f);

            var bg = go.AddComponent<Image>();
            bg.color = GameTheme.SliderTrack;

            var viewport = Node("Viewport", go.transform, out RectTransform viewportRect);
            Stretch(viewportRect, 6f, 2f);
            viewport.AddComponent<RectMask2D>();

            var text = Label("Text", viewport.transform, value, 14f, GameTheme.Text);
            Stretch(text.rectTransform, 0f, 0f);
            text.raycastTarget = true;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.text = value;
            input.onEndEdit.AddListener(v => onChanged(v));
            return input;
        }

        public static void Stretch(RectTransform rect, float padX, float padY)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }

        /// Uses the game's EventSystem when there is one. Creating a second breaks
        /// input for both, so never add one unconditionally.
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("CkQolEventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            Debug.Log("[CkQol] no EventSystem present, created one");
        }
    }
}
