using System;
using UnityEngine;

namespace CkQol.Native
{
    /// A line in the game's bottom-right key hints.
    ///
    /// InGameButtonHintsUI.LateUpdate walks hintButtonRows and lays out whichever
    /// entries report isButtonActive, so an added hint is positioned by the game.
    public class CkQolHint : IngameButtonHint
    {
        /// Rebuilt each update so a rebound key shows immediately.
        public Func<string> Label;

        public Func<bool> Visible;

        private PugText _text;
        private PugText[] _otherTexts;
        private SpriteRenderer[] _sprites;

        /// A stock hint in the same row, whose scale is mirrored.
        private IngameButtonHint _sibling;
        private bool _active;
        private bool _initialized;
        private int _reports;
        private float _nextReport;
        private string _shown;

        public override bool isButtonActive => _active;

        internal void Bind(PugText text, PugText[] otherTexts, SpriteRenderer[] sprites,
                           IngameButtonHint sibling)
        {
            _text = text;
            _otherTexts = otherTexts;
            _sprites = sprites;
            _sibling = sibling;
        }

        public override void UpdateVisuals()
        {
            // Mirrored from a stock hint rather than read from
            // Manager.ui.CalcGameplayUITargetScaleMultiplier(): that call returns zero
            // for this component, while the hints beside it are scaled correctly. The
            // sibling is the value we actually want anyway.
            transform.localScale = _sibling != null
                ? _sibling.transform.localScale
                : Manager.ui.CalcGameplayUITargetScaleMultiplier();

            bool visible = Visible != null && Visible() &&
                           !Manager.ui.isAnyInventoryShowing && !Manager.ui.isShowingMap;

            // Activation first: PugText releases its glyphs to the pool when disabled
            // (PugText.cs:307-308), so text rendered into a disabled object is dropped
            // and comes back blank.
            if (visible != _active || !_initialized)
            {
                // The whole chain, not just the label: it is a child of textContainer,
                // which every stock hint toggles (HonkButton.cs:40) and which the donor
                // was not showing when it was cloned. An active child inside an
                // inactive parent renders nothing.
                for (var at = _text != null ? _text.transform : null;
                     at != null && at != transform;
                     at = at.parent)
                {
                    at.gameObject.SetActive(visible);
                }

                // Renderers are disabled rather than their GameObjects deactivated: the
                // donor's sprites can be the label's own parent or the hint root, and
                // deactivating those takes the label down with them.
                foreach (var sprite in _sprites)
                {
                    if (sprite != null) sprite.enabled = false;
                }

                foreach (var text in _otherTexts)
                {
                    if (text != null) GameMenu.SetLiteral(text, string.Empty);
                }

                // Re-render on the way back in: the glyphs were freed on the way out.
                _shown = null;
                _active = visible;
                _initialized = true;
            }

            if (visible && _text != null)
            {
                string label = Label != null ? Label() : string.Empty;
                if (label != _shown)
                {
                    GameMenu.SetLiteral(_text, label);
                    _shown = label;
                }
            }

            if (visible && Time.unscaledTime >= _nextReport && _reports < 5)
            {
                _reports++;
                _nextReport = Time.unscaledTime + 2f;
                Debug.Log($"[CkQol] hint scale={transform.localScale} " +
                          $"sibling={(_sibling != null ? _sibling.transform.localScale.ToString() : "none")} " +
                          $"local={transform.localPosition} label='{_shown}' " +
                          $"glyphs={(_text != null ? _text.glyphs.Count : -1)} " +
                          $"textActive={(_text != null && _text.gameObject.activeInHierarchy)} " +
                          $"rootActive={gameObject.activeInHierarchy}");
            }

            base.LateUpdate();
        }

        // The hint is not interactive; the base class would otherwise treat it as a
        // clickable UI element.
        public override void OnSelected() { }
        public override void OnDeselected(bool playEffect = true) { }
        public override void OnLeftClicked(bool mod1, bool mod2) { }
        public override void OnRightClicked(bool mod1, bool mod2) { }
    }

    public static class GameHints
    {
        /// Adds a hint beside the game's own, cloned from one of them so it inherits
        /// the font, scale and placement. Returns null while the HUD does not exist
        /// yet, so callers should keep trying.
        ///
        /// The key glyph sprites are bound to Rewired actions, which a mod cannot add,
        /// so the icon is hidden and the key is spelled out in the label instead.
        public static CkQolHint Install(Func<string> label, Func<bool> visible)
        {
            var hintsUI = UnityEngine.Object.FindFirstObjectByType<InGameButtonHintsUI>();
            if (hintsUI == null || hintsUI.hintButtonRows == null) return null;

            InGameButtonHintsUI.InGameHintButtons row = null;
            foreach (var candidate in hintsUI.hintButtonRows)
            {
                if (candidate != null && candidate.buttons != null && candidate.buttons.Count > 0)
                {
                    row = candidate;
                    break;
                }
            }
            if (row == null) return null;

            IngameButtonHint donor = null;
            foreach (var candidate in row.buttons)
            {
                if (candidate != null) { donor = candidate; break; }
            }
            if (donor == null) return null;

            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject, Staging);
                clone.name = "CkQolHint";

                foreach (var stale in clone.GetComponentsInChildren<IngameButtonHint>(true))
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }

                var texts = clone.GetComponentsInChildren<PugText>(true);
                if (texts.Length == 0)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    return null;
                }

                // Everything except the one label: the donor's own wording and its
                // key glyph, neither of which means anything here.
                var sprites = clone.GetComponentsInChildren<SpriteRenderer>(true);
                var others = new PugText[texts.Length - 1];
                for (int i = 1; i < texts.Length; i++) others[i - 1] = texts[i];

                // The donor's width is sized for labels like "Tab", and PugFont only
                // wraps when maxWidth is above zero (PugFont.cs:141).
                texts[0].maxWidth = 0f;
                foreach (var other in others) other.maxWidth = 0f;

                var hint = clone.AddComponent<CkQolHint>();
                hint.Bind(texts[0], others, sprites, donor);
                hint.Label = label;
                hint.Visible = visible;

                clone.transform.SetParent(donor.transform.parent, false);
                clone.SetActive(true);
                row.buttons.Add(hint);

                var report = new System.Text.StringBuilder();
                report.Append("[CkQol] hint donor '").Append(donor.name)
                      .Append("' texts=").Append(texts.Length)
                      .Append(" sprites=").Append(sprites.Length).Append(" |");
                foreach (var text in texts) report.Append(" T:").Append(Path(text.transform, clone.transform));
                foreach (var sprite in sprites) report.Append(" S:").Append(Path(sprite.transform, clone.transform));
                Debug.Log(report.ToString());

                Debug.Log("[CkQol] added a key hint to the HUD");
                return hint;
            }
            catch (Exception e)
            {
                Debug.LogError("[CkQol] failed to add the key hint");
                Debug.LogException(e);
                return null;
            }
        }

        /// Hierarchy path of a child relative to the hint root, for the one-off
        /// structure log - the donor's layout is authored, not visible in code.
        private static string Path(Transform child, Transform root)
        {
            string path = child.name;
            for (var at = child.parent; at != null && at != root; at = at.parent)
            {
                path = at.name + "/" + path;
            }
            return path;
        }

        /// Instantiating into an active parent would run the donor's Awake, which reads
        /// the equipped item before the script is swapped.
        private static Transform _staging;

        private static Transform Staging
        {
            get
            {
                if (_staging != null) return _staging;
                var go = new GameObject("CkQolHintStaging");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _staging = go.transform;
                return _staging;
            }
        }
    }
}
