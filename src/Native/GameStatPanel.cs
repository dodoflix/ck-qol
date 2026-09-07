using System;
using System.Collections.Generic;
using UnityEngine;

namespace CkQol.Native
{
    /// One line of a stat panel: a small item sprite and a number.
    public struct StatRow
    {
        public Sprite Icon;
        public string Text;
    }

    /// A stack of icon-and-number rows in the top-left HUD, under the minion counter.
    ///
    /// The HUD is a fixed world-space layout with no screen-corner maths anywhere, so
    /// placement follows MinionCountUI.cs:51-54: take x from the widget above and
    /// rewrite y every frame.
    public class CkQolStatPanel : MonoBehaviour
    {
        /// Rebuilt each frame. An empty list takes the panel off screen.
        public Func<List<StatRow>> Rows;

        /// 15px, the step the HUD stacks its own widgets by.
        private const float RowStep = 0.9375f;

        /// Gap between the icon and the number it belongs to.
        private const float IconGap = 0.75f;

        /// The HUD's own size for a line of stats; the donor's is sized for the hover
        /// window and reads as oversized here.
        private const TextManager.FontFace Font = TextManager.FontFace.thinSmall;

        private const int MaxRows = 8;

        private class PanelRow
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public PugText Text;
            public string Shown;
        }

        private MinionCountUI _anchor;
        private HoverRequiredMaterialUIElement _donor;
        private readonly List<PanelRow> _pool = new List<PanelRow>();

        private bool _active;
        private bool _initialized;

        internal void Bind(MinionCountUI anchor, HoverRequiredMaterialUIElement donor)
        {
            _anchor = anchor;
            _donor = donor;
        }

        private void LateUpdate()
        {
            var rows = InGame() && Rows != null ? Rows() : null;
            bool visible = rows != null && rows.Count > 0;

            if (visible != _active || !_initialized) Show(visible);
            if (!visible) return;

            Vector3 scale = Manager.ui.CalcGameplayUITargetScaleMultiplier();
            transform.localScale = scale;
            Place(scale);

            int shown = rows.Count < MaxRows ? rows.Count : MaxRows;
            for (int i = 0; i < shown; i++) Draw(RowAt(i), rows[i], i);
            for (int i = shown; i < _pool.Count; i++) Blank(_pool[i]);
        }

        private static bool InGame()
        {
            return Manager.sceneHandler != null && Manager.sceneHandler.isInGame &&
                   Manager.main != null && Manager.main.player != null;
        }

        /// Under the minion counter, or in its place: it hides itself entirely when the
        /// player has no minions (MinionCountUI.cs:33-37).
        private void Place(Vector3 scale)
        {
            var container = _anchor != null ? _anchor.container : null;
            if (container == null) return;

            Vector3 at = container.transform.position;

            // Scaled, unlike the game's own raw offsets, so the gap holds at any UI size.
            float y = container.activeSelf
                ? at.y - RowStep * scale.y
                : (_anchor.conditionsContainerUI != null
                    ? _anchor.conditionsContainerUI.GetBottomPosition()
                    : at.y);

            transform.position = new Vector3(at.x, y, at.z);
        }

        private void Draw(PanelRow row, StatRow data, int index)
        {
            if (row == null) return;

            row.Root.transform.localPosition = new Vector3(0f, -RowStep * index, 0f);

            if (row.Shown != data.Text)
            {
                GameMenu.SetLiteral(row.Text, data.Text);
                row.Shown = data.Text;
            }

            row.Icon.sprite = data.Icon;
            row.Icon.enabled = data.Icon != null;

            // The donor is the crafting hover's ingredient count, which the game tints
            // for whether you have enough of a material.
            GameMenu.Tint(row.Text, Color.white);
        }

        private void Blank(PanelRow row)
        {
            if (row == null || row.Shown == string.Empty) return;

            GameMenu.SetLiteral(row.Text, string.Empty);
            row.Icon.enabled = false;
            row.Shown = string.Empty;
        }

        private void Show(bool visible)
        {
            // PugText releases its glyphs to the pool when disabled (PugText.cs:307-308),
            // so everything has to be rendered again on the way back in.
            foreach (var row in _pool) row.Shown = null;

            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(visible);
            }

            _active = visible;
            _initialized = true;
        }

        private PanelRow RowAt(int index)
        {
            while (_pool.Count <= index)
            {
                var row = Build();
                if (row == null) return null;
                _pool.Add(row);
            }
            return _pool[index];
        }

        private PanelRow Build()
        {
            var clone = UnityEngine.Object.Instantiate(_donor.gameObject, GameMenu.Staging);
            clone.name = "CkQolStatRow";

            var element = clone.GetComponent<HoverRequiredMaterialUIElement>();
            if (element == null || element.amountNumber == null || element.SR == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                Debug.LogError("[CkQol] stat row donor is missing its icon or number");
                return null;
            }

            // Would keep re-rendering our number to a platform key
            // (PlatformDependentPugText.cs:171-176).
            foreach (var stale in clone.GetComponentsInChildren<PlatformDependentPugText>(true))
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }

            GameMenu.DropStrayGlyphs(clone, element.SR, element.chestIcon);


            // The donor's width is sized for the hover window, and PugFont only wraps
            // above zero (PugFont.cs:141). Alignment is forced rather than inherited so
            // the number starts on its own transform and centres on it, which is what
            // the icon is then placed against. Render re-reads style.fontFace
            // (PugText.cs:700), so setting it here is enough.
            foreach (var text in clone.GetComponentsInChildren<PugText>(true))
            {
                text.maxWidth = 0f;
                GameMenu.KeepRendered(text);
                text.style.fontFace = Font;
                text.style.horizontalAlignment = PugTextStyle.HorizontalAlignment.left;
                text.style.verticalAlignment = PugTextStyle.VerticalAlignment.center;
            }

            if (element.chestIcon != null) element.chestIcon.enabled = false;

            // The donor carries wherever the hover window last placed it.
            if (element.container != null)
            {
                element.container.transform.localPosition = Vector3.zero;
            }

            // Set in world space while the clone is unscaled in staging: the icon and the
            // number sit at different depths, so their local values are not comparable.
            Vector3 number = element.amountNumber.transform.position;
            element.SR.transform.position =
                new Vector3(number.x - IconGap, number.y, element.SR.transform.position.z);

            element.SR.color = Color.white;
            element.amountNumber.SetTempColor(Color.white);

            if (element.container != null) element.container.SetActive(true);
            clone.transform.SetParent(transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.SetActive(true);

            // Everything the ingredient row draws that a stat row does not - the
            // material name, and the "also in a chest nearby" icon and count - and
            // only now the clone is active. PugText drops a render made while its
            // object is disabled, so blanking in staging is thrown away and the
            // donor's own text comes back the moment the row is switched on.
            foreach (var text in clone.GetComponentsInChildren<PugText>(true))
            {
                GameMenu.SetLiteral(text, string.Empty);
            }

            return new PanelRow
            {
                Root = clone,
                Icon = element.SR,
                Text = element.amountNumber,
            };
        }
    }

    public static class GameStatPanel
    {
        /// Adds a panel under the minion counter. Returns null while the HUD does not
        /// exist yet, so callers should keep trying.
        public static CkQolStatPanel Install(Func<List<StatRow>> rows)
        {
            var anchor = UnityEngine.Object.FindFirstObjectByType<MinionCountUI>();
            if (anchor == null || anchor.container == null) return null;

            var mouse = Manager.ui != null ? Manager.ui.mouse : null;
            if (mouse == null || mouse.hoverMaterials == null) return null;

            HoverRequiredMaterialUIElement donor = null;
            foreach (var candidate in mouse.hoverMaterials)
            {
                if (candidate == null) continue;
                donor = candidate;
                break;
            }
            if (donor == null) return null;

            try
            {
                var root = new GameObject("CkQolStatPanel");
                root.layer = anchor.container.layer;
                root.transform.SetParent(anchor.container.transform.parent, false);

                var panel = root.AddComponent<CkQolStatPanel>();
                panel.Bind(anchor, donor);
                panel.Rows = rows;

                Debug.Log("[CkQol] added the stat panel to the HUD");
                return panel;
            }
            catch (Exception e)
            {
                Debug.LogError("[CkQol] failed to add the stat panel");
                Debug.LogException(e);
                return null;
            }
        }
    }
}
