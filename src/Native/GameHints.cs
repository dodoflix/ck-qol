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
        private GameObject[] _hidden;
        private bool _active;
        private string _shown;
        private Vector3 _scale;

        public override bool isButtonActive => _active;

        internal void Bind(PugText text, GameObject[] hidden)
        {
            _text = text;
            _hidden = hidden;
        }

        public override void UpdateVisuals()
        {
            // Every hint does this; without it the row does not follow the UI scale
            // setting.
            Vector3 scale = Manager.ui.CalcGameplayUITargetScaleMultiplier();
            if (scale != _scale)
            {
                transform.localScale = scale;
                _scale = scale;
            }

            bool visible = Visible != null && Visible() &&
                           !Manager.ui.isAnyInventoryShowing && !Manager.ui.isShowingMap;

            if (visible && _text != null)
            {
                string label = Label != null ? Label() : string.Empty;
                if (label != _shown)
                {
                    GameMenu.SetLiteral(_text, label);
                    _shown = label;
                }
            }

            if (visible != _active)
            {
                if (_text != null) _text.gameObject.SetActive(visible);
                foreach (var go in _hidden)
                {
                    if (go != null) go.SetActive(false);
                }
                _active = visible;
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

                // Everything except the one label: the donor's own text and its key
                // glyph, neither of which means anything here.
                var sprites = clone.GetComponentsInChildren<SpriteRenderer>(true);
                var hidden = new GameObject[texts.Length - 1 + sprites.Length];
                int at = 0;
                for (int i = 1; i < texts.Length; i++) hidden[at++] = texts[i].gameObject;
                foreach (var sprite in sprites) hidden[at++] = sprite.gameObject;

                var hint = clone.AddComponent<CkQolHint>();
                hint.Bind(texts[0], hidden);
                hint.Label = label;
                hint.Visible = visible;

                clone.transform.SetParent(donor.transform.parent, false);
                clone.SetActive(true);
                row.buttons.Add(hint);

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
