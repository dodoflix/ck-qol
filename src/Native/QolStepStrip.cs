using System.Collections.Generic;
using UnityEngine;

namespace CkQol.Native
{
    /// One clickable box per diamond, laid over a row's value text.
    ///
    /// This is how the game does it: the audio rows carry a child ButtonUIElement
    /// per step whose UnityEvents call back into the row with a baked index. Those
    /// are authored in the prefab, and a persistent call's target cannot be
    /// re-pointed at runtime through any public API - so the strip is rebuilt here
    /// with a subclass instead. Same mechanism, constructed rather than serialised.
    public class QolStepStrip : MonoBehaviour
    {
        private readonly List<QolStepButton> _buttons = new List<QolStepButton>();
        private PugText _value;
        private Rect _placed;
        private bool _logged;

        public static void Build(QolNumberOption row, int steps)
        {
            if (row == null || row.valueText == null || steps <= 0) return;

            var strip = row.gameObject.AddComponent<QolStepStrip>();
            strip._value = row.valueText;

            for (int i = 0; i < steps; i++)
            {
                var go = new GameObject("CkQolStep" + i);
                go.SetActive(false);

                // The click raycast is masked to the UI layer. Taking the row's layer
                // rather than naming one keeps this correct without depending on the
                // layer index.
                go.layer = row.gameObject.layer;
                go.transform.SetParent(row.transform, false);

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;

                var button = go.AddComponent<QolStepButton>();
                button.Row = row;

                // Matches the game's numbering: glyph i is lit when step > i, so the
                // box under the first diamond carries 1.
                button.Step = i + 1;

                // Hovering a diamond also selects the row, so it highlights as a whole.
                button.optionToSelectOnHover = row;

                go.SetActive(true);
                strip._buttons.Add(button);
            }
        }

        /// Follows the value text, which is re-rendered on every change and re-laid
        /// out when the menu opens or the language changes.
        private void LateUpdate()
        {
            if (_value == null || _buttons.Count == 0) return;

            Rect dims = _value.dimensions;
            if (dims == _placed) return;
            _placed = dims;

            Vector3 origin = _value.transform.position;
            Vector3 min = origin + (Vector3)dims.min;
            Vector3 max = origin + (Vector3)dims.max;

            float width = (max.x - min.x) / _buttons.Count;
            if (width <= 0f) return;

            float height = max.y - min.y;
            float centreY = (min.y + max.y) * 0.5f;

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[CkQol] step strip on '{name}': boxes={_buttons.Count} " +
                          $"width={width:0.###} height={height:0.###} layer={gameObject.layer}");
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i];
                if (button == null) continue;

                Vector3 world = new Vector3(min.x + width * (i + 0.5f), centreY, origin.z);
                Vector3 local = button.transform.parent.InverseTransformPoint(world);

                // In front of the row's own collider. That box spans the whole row
                // from the label's left edge to the value's right edge, so it covers
                // the diamonds too - and UIMouse keeps the nearest hit, so the step
                // boxes only win if they sit closer to the camera.
                local.z -= 0.5f;

                button.transform.localPosition = local;
                var box = button.GetComponent<BoxCollider>();
                if (box != null) box.size = new Vector3(width, height, 0.25f);
            }
        }
    }

    /// One diamond's worth of the strip.
    public class QolStepButton : ButtonUIElement
    {
        public QolNumberOption Row;
        public int Step;

        protected override void Awake()
        {
            // ButtonUIElement.Awake walks both sprite lists. A component added at
            // runtime has no serialised value for them, so they arrive null.
            if (spritesShownUnpressed == null) spritesShownUnpressed = new List<SpriteRenderer>();
            if (spritesShownPressed == null) spritesShownPressed = new List<SpriteRenderer>();
            base.Awake();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            // No matching OnDeselected: the game's volume rows leave the highlight
            // alone when a step button is deselected and reset it only when the whole
            // row loses selection, so moving between diamonds does not flicker.
            if (Row != null) Row.PreviewStep(Step);
        }

        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            base.OnLeftClicked(mod1, mod2);
            if (Row != null) Row.SetStep(Step);
        }
    }
}
