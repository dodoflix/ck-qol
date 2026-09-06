using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace CkQol.UI
{
    /// Borrows Core Keeper's own font and UI sprites so the menu matches the game.
    ///
    /// Everything is looked up at runtime from already-loaded assets rather than
    /// bundled: PugMod script mods ship source only, so we cannot carry assets, and
    /// the game's atlases are not addressable by a stable path across versions.
    /// Every lookup degrades to a plain fallback if the asset is missing, because a
    /// menu that renders unstyled is still usable and a null deref is not.
    public static class GameTheme
    {
        // Sampled from Core Keeper's UI: dark stone panel, warm parchment text.
        public static readonly Color PanelBg = new Color32(38, 33, 44, 240);
        public static readonly Color PanelBorder = new Color32(88, 76, 92, 255);
        public static readonly Color TabIdle = new Color32(52, 45, 60, 255);
        public static readonly Color TabHover = new Color32(72, 62, 82, 255);
        public static readonly Color TabActive = new Color32(96, 82, 105, 255);
        public static readonly Color Text = new Color32(232, 220, 202, 255);
        public static readonly Color TextDim = new Color32(158, 146, 136, 255);
        public static readonly Color Accent = new Color32(226, 174, 78, 255);
        public static readonly Color SliderTrack = new Color32(26, 22, 30, 255);

        private static TMP_FontAsset _font;
        private static bool _fontSearched;
        private static Sprite _panelSprite;
        private static bool _panelSearched;
        private static Texture2D _white;

        /// The game's UI font. Picks whichever TMP font the game has actually loaded,
        /// preferring one whose name looks like Core Keeper's own rather than TMP's
        /// bundled Liberation Sans default.
        public static TMP_FontAsset Font
        {
            get
            {
                if (_fontSearched) return _font;
                _fontSearched = true;

                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .Where(f => f != null && f.name != null)
                    .ToList();

                if (fonts.Count == 0)
                {
                    Debug.LogWarning("[CkQol] no TMP font found, using TMP default");
                    _font = TMP_Settings.defaultFontAsset;
                    return _font;
                }

                _font = fonts.FirstOrDefault(f => !f.name.Contains("Liberation"))
                        ?? fonts[0];

                Debug.Log($"[CkQol] using font '{_font.name}' ({fonts.Count} loaded: " +
                          string.Join(", ", fonts.Take(8).Select(f => f.name)) + ")");
                return _font;
            }
        }

        /// A 9-sliced panel sprite from the game, if we can find a plausible one.
        /// Null is fine - callers fall back to a flat colour.
        public static Sprite PanelSprite
        {
            get
            {
                if (_panelSearched) return _panelSprite;
                _panelSearched = true;

                var candidates = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(s => s != null && s.name != null && s.border != Vector4.zero)
                    .ToList();

                // Prefer something that names itself a panel/window/frame and is 9-sliced.
                string[] wanted = { "panel", "window", "frame", "box", "bg", "background" };
                _panelSprite = candidates.FirstOrDefault(
                    s => wanted.Any(w => s.name.ToLowerInvariant().Contains(w)));

                if (_panelSprite != null)
                {
                    Debug.Log($"[CkQol] using panel sprite '{_panelSprite.name}'");
                }
                return _panelSprite;
            }
        }

        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
                return _white;
            }
        }

        /// Logs what UI assets are available. Handy when tuning the look against a
        /// new game version - run once, read the log, pick better sprite names.
        public static void DumpAssets(int limit = 40)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Select(f => f.name).Distinct().ToList();
            Debug.Log($"[CkQol] TMP fonts ({fonts.Count}): {string.Join(", ", fonts.Take(limit))}");

            var sliced = Resources.FindObjectsOfTypeAll<Sprite>()
                .Where(s => s != null && s.border != Vector4.zero)
                .Select(s => s.name).Distinct().Take(limit).ToList();
            Debug.Log($"[CkQol] 9-sliced sprites ({sliced.Count} shown): {string.Join(", ", sliced)}");
        }

        public static void ApplyText(TextMeshProUGUI text, float size, Color color,
                                     TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            if (Font != null) text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.richText = true;
            text.raycastTarget = false;
        }
    }
}
