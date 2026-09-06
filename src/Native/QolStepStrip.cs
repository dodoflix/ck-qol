using System.Collections.Generic;
using UnityEngine;

namespace CkQol.Native
{
    /// One clickable box per diamond, over a row's value text.
    ///
    /// The audio rows do this with prefab-authored child ButtonUIElements carrying a
    /// baked index. A persistent call's target cannot be re-pointed at runtime, so
    /// the strip is rebuilt with a subclass - same mechanism, constructed.
    public class QolStepStrip : MonoBehaviour
    {
        private readonly List<QolStepButton> _buttons = new List<QolStepButton>();
        private PugText _value;
        private Rect _placed;

        public static void Build(QolNumberOption row, int steps)
        {
            if (row == null || row.valueText == null || steps <= 0) return;

            var strip = row.gameObject.AddComponent<QolStepStrip>();
            strip._value = row.valueText;

            for (int i = 0; i < steps; i++)
            {
                var go = new GameObject("CkQolStep" + i);
                go.SetActive(false);

                // The click raycast is masked to the UI layer; take the row's rather
                // than hardcoding the index.
                go.layer = row.gameObject.layer;
                go.transform.SetParent(row.transform, false);

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;

                var button = go.AddComponent<QolStepButton>();
                button.Row = row;

                // Glyph i is lit when step > i, so the first box carries 1.
                button.Step = i + 1;

                // Hovering a diamond selects the row too, so it highlights.
                button.optionToSelectOnHover = row;

                go.SetActive(true);
                strip._buttons.Add(button);
            }
        }

        /// Follows the value text, re-rendered on every change.
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

            for (int i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i];
                if (button == null) continue;

                Vector3 world = new Vector3(min.x + width * (i + 0.5f), centreY, origin.z);
                Vector3 local = button.transform.parent.InverseTransformPoint(world);

                // In front of the row's own collider, which spans label to value:
                // UIMouse keeps the nearest hit.
                local.z -= 0.5f;

                button.transform.localPosition = local;
                var box = button.GetComponent<BoxCollider>();
                if (box != null) box.size = new Vector3(width, height, 0.25f);
            }
        }
    }

    /// One diamond.
    public class QolStepButton : ButtonUIElement
    {
        public QolNumberOption Row;
        public int Step;

        protected override void Awake()
        {
            // Awake walks both lists; a runtime-added component has them null.
            if (spritesShownUnpressed == null) spritesShownUnpressed = new List<SpriteRenderer>();
            if (spritesShownPressed == null) spritesShownPressed = new List<SpriteRenderer>();
            base.Awake();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            // No OnDeselected: volume rows reset only when the whole row is
            // deselected, so moving between diamonds does not flicker.
            if (Row != null) Row.PreviewStep(Step);
        }

        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            base.OnLeftClicked(mod1, mod2);
            if (Row != null) Row.SetStep(Step);
        }
    }
}
