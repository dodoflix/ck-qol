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

        /// Sets a sprite on the hint's icon, or nulls it for no icon. Called every
        /// update while visible, so an implementation that costs anything should cache.
        public Action<SpriteRenderer> Icon;

        /// Colour for the icon and the label, for showing state without wording.
        public Func<Color> Tint;

        private PugText _text;

        /// The donor's outline copies of the label, drawn behind it as a shadow. They
        /// carry the same string.
        private PugText[] _shadows;
        private SpriteRenderer[] _otherSprites;
        private SpriteRenderer _icon;

        /// A stock hint in the same row, whose scale is mirrored. Simpler than
        /// recomputing it, and cannot disagree with the hints beside it.
        private IngameButtonHint _sibling;

        private bool _active;
        private bool _initialized;
        private string _shown;

        /// Where the donor put the icon, and whether it has been moved for the current
        /// label width.
        private float _iconRestX;
        private bool _placed;

        public override bool isButtonActive => _active;

        internal void Bind(PugText text, PugText[] shadows, SpriteRenderer[] otherSprites,
                           IngameButtonHint sibling, SpriteRenderer icon)
        {
            _text = text;
            _shadows = shadows;
            _otherSprites = otherSprites;
            _sibling = sibling;
            _icon = icon;
            if (icon != null) _iconRestX = icon.transform.localPosition.x;
        }

        public override void UpdateVisuals()
        {
            transform.localScale = _sibling != null
                ? _sibling.transform.localScale
                : Manager.ui.CalcGameplayUITargetScaleMultiplier();

            bool visible = Visible != null && Visible() &&
                           !Manager.ui.isAnyInventoryShowing && !Manager.ui.isShowingMap;

            if (visible != _active || !_initialized) Show(visible);

            if (visible)
            {
                // Applied every frame rather than on change. Render rebuilds the glyphs
                // from the style, losing any colour on them, and does not always have
                // them ready on the frame it is called - so a one-shot recolour can run
                // over an empty list and never retry.
                Color tint = Tint != null ? Tint() : Color.white;

                if (_text != null)
                {
                    string label = Label != null ? Label() : string.Empty;

                    // Compared against what is actually on screen, not just against the
                    // last value set: the donor's own components re-render this text on
                    // menu transitions, and the label has to win that back.
                    if (label != _shown || _text.GetText() != label)
                    {
                        GameMenu.SetLiteral(_text, label);
                        foreach (var shadow in _shadows)
                        {
                            if (shadow != null) GameMenu.SetLiteral(shadow, label);
                        }
                        _shown = label;
                        _placed = false;
                    }

                    Recolour(_text, tint);
                }

                if (_icon != null)
                {
                    Icon?.Invoke(_icon);
                    _icon.enabled = _icon.sprite != null;
                    _icon.color = tint;

                    // Sits left of the label rather than at the donor's position, which
                    // assumed the donor's own text width.
                    if (!_placed && _icon.enabled && _text != null)
                    {
                        _placed = true;
                        var at = _icon.transform.localPosition;
                        at.x = _iconRestX - _text.dimensions.width;
                        _icon.transform.localPosition = at;
                    }
                }
            }

            base.LateUpdate();
        }

        private static void Recolour(PugText text, Color tint)
        {
            var glyphs = text.glyphs;
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i] != null) glyphs[i].color = tint;
            }
        }

        private void Show(bool visible)
        {
            // The label's whole parent chain, not just the label: it is a child of
            // textContainer, which every stock hint toggles (HonkButton.cs:40) and which
            // the donor was not showing when cloned. An active child inside an inactive
            // parent renders nothing.
            for (var at = _text != null ? _text.transform : null;
                 at != null && at != transform;
                 at = at.parent)
            {
                at.gameObject.SetActive(visible);
            }

            // Renderers are disabled rather than their GameObjects deactivated: the
            // donor's sprites can be the label's own parent or the hint root, and
            // deactivating those takes the label down with them.
            foreach (var sprite in _otherSprites)
            {
                if (sprite != null) sprite.enabled = false;
            }
            if (_icon != null && !visible) _icon.enabled = false;

            // Re-render on the way back in: PugText releases its glyphs to the pool when
            // disabled (PugText.cs:307-308), so the old ones are gone.
            _shown = null;
            _placed = false;
            _active = visible;
            _initialized = true;
        }

        // Not interactive; the base class would otherwise treat it as a clickable UI
        // element.
        public override void OnSelected() { }
        public override void OnDeselected(bool playEffect = true) { }
        public override void OnLeftClicked(bool mod1, bool mod2) { }
        public override void OnRightClicked(bool mod1, bool mod2) { }
    }

    public static class GameHints
    {
        /// Adds a hint beside the game's own, cloned from one of them so it inherits the
        /// font, scale and placement. Returns null while the HUD does not exist yet, so
        /// callers should keep trying.
        ///
        /// Appended, never inserted: the row's anchor is read live from buttons[0]
        /// (InGameButtonHintsUI.cs:40), so taking that position would move the whole row.
        public static CkQolHint Install(Func<string> label, Func<bool> visible)
        {
            var hintsUI = UnityEngine.Object.FindFirstObjectByType<InGameButtonHintsUI>();
            if (hintsUI == null || hintsUI.hintButtonRows == null) return null;

            IngameButtonHint donor = null;
            InGameButtonHintsUI.InGameHintButtons row = null;

            foreach (var candidate in hintsUI.hintButtonRows)
            {
                if (candidate == null || candidate.buttons == null) continue;

                foreach (var button in candidate.buttons)
                {
                    if (button == null) continue;
                    donor = button;
                    row = candidate;
                    break;
                }
                if (donor != null) break;
            }
            if (donor == null) return null;

            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject, GameMenu.Staging);
                clone.name = "CkQolHint";

                foreach (var stale in clone.GetComponentsInChildren<IngameButtonHint>(true))
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }

                // Would keep re-rendering the label to the donor's own key character
                // (PlatformDependentPugText.cs:171-176), which shows through as the
                // donor's text until whatever it resolves to stops changing.
                foreach (var stale in clone.GetComponentsInChildren<PlatformDependentPugText>(true))
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }

                var texts = clone.GetComponentsInChildren<PugText>(true);
                if (texts.Length == 0)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    Debug.LogError("[CkQol] hint donor has no text to use as a label");
                    return null;
                }

                // texts[0] is the label and the rest are its outline copies, which draw
                // the shadow and take the same string. The donor's width is sized for
                // labels like "Tab", and PugFont only wraps above zero (PugFont.cs:141).
                var shadows = new PugText[texts.Length - 1];
                for (int i = 1; i < texts.Length; i++) shadows[i - 1] = texts[i];
                foreach (var text in texts) text.maxWidth = 0f;

                // The donor's icon is reused for ours; everything else it draws, such as
                // the light-up Flare, stays off.
                SpriteRenderer icon = null;
                var sprites = clone.GetComponentsInChildren<SpriteRenderer>(true);
                int others = 0;
                foreach (var sprite in sprites)
                {
                    if (icon == null && sprite != null && sprite.name == "Icon") icon = sprite;
                    else others++;
                }

                var otherSprites = new SpriteRenderer[others];
                int at = 0;
                foreach (var sprite in sprites)
                {
                    if (sprite != icon) otherSprites[at++] = sprite;
                }

                var hint = clone.AddComponent<CkQolHint>();
                hint.Bind(texts[0], shadows, otherSprites, donor, icon);
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

    }
}
