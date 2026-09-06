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

                Debug.Log($"[CkQol] using font '{_font.name}' nativeSize={NativeSize} " +
                          $"({fonts.Count} loaded: " +
                          string.Join(", ", fonts.Take(8).Select(f => f.name)) + ")");
                return _font;
            }
        }

        private static Dictionary<string, Sprite> _index;

        /// Every loaded sprite by name. Built once - the game has thousands, and
        /// FindObjectsOfTypeAll is far too slow to call per widget.
        private static Dictionary<string, Sprite> Index
        {
            get
            {
                if (_index != null) return _index;
                _index = new Dictionary<string, Sprite>();
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
                    _index[sprite.name] = sprite;
                }
                Debug.Log($"[CkQol] indexed {_index.Count} sprites");
                return _index;
            }
        }

        /// First of these names that is loaded. Names come from the game's own
        /// resources.assets, so they are exact rather than guessed by keyword.
        public static Sprite FindSprite(params string[] names)
        {
            foreach (var name in names)
            {
                if (Index.TryGetValue(name, out var sprite) && sprite != null) return sprite;
            }
            return null;
        }

        /// Window and panel shell. Must be 9-sliced or it stretches into mush, so a
        /// sprite without borders is rejected in favour of a flat colour.
        public static Sprite PanelSprite
        {
            get
            {
                if (_panelSearched) return _panelSprite;
                _panelSearched = true;
                var found = FindSprite("32x32_menu_border", "32x32_itemui_border", "32x32_map_border");
                if (found != null && found.border == Vector4.zero)
                {
                    Debug.Log($"[CkQol] '{found.name}' has no 9-slice border, using flat panels");
                    found = null;
                }
                _panelSprite = found;
                if (_panelSprite != null) Debug.Log($"[CkQol] panel sprite '{_panelSprite.name}'");
                return _panelSprite;
            }
        }

        private static Sprite _buttonSprite;
        private static bool _buttonSearched;

        /// Button face.
        public static Sprite ButtonSprite
        {
            get
            {
                if (_buttonSearched) return _buttonSprite;
                _buttonSearched = true;
                var found = FindSprite("base_button_1", "base_button_2", "button_1");
                if (found != null && found.border == Vector4.zero) found = null;
                _buttonSprite = found;
                if (_buttonSprite != null) Debug.Log($"[CkQol] button sprite '{_buttonSprite.name}'");
                return _buttonSprite;
            }
        }

        private static Sprite _tabSprite, _checkboxSprite, _checkmarkSprite, _knobSprite;
        private static bool _tabSearched, _checkboxSearched, _checkmarkSearched, _knobSearched;

        /// Tab buttons. The game styles tabs differently from ordinary buttons, so
        /// reusing the button face here would look subtly wrong.
        public static Sprite TabSprite
        {
            get
            {
                if (_tabSearched) return _tabSprite;
                _tabSearched = true;
                _tabSprite = FindSprite("tab_ui", "tab_ui_0", "tab_ui_2");
                if (_tabSprite != null) Debug.Log($"[CkQol] tab sprite '{_tabSprite.name}'");
                return _tabSprite;
            }
        }

        /// Checkbox frame. An icon, not a 9-slice, so it is drawn Simple at a fixed
        /// square size rather than stretched.
        public static Sprite CheckboxSprite
        {
            get
            {
                if (_checkboxSearched) return _checkboxSprite;
                _checkboxSearched = true;
                _checkboxSprite = FindSprite("checkBox_0", "checkBox_1");
                if (_checkboxSprite != null) Debug.Log($"[CkQol] checkbox sprite '{_checkboxSprite.name}'");
                return _checkboxSprite;
            }
        }

        public static Sprite CheckmarkSprite
        {
            get
            {
                if (_checkmarkSearched) return _checkmarkSprite;
                _checkmarkSearched = true;
                _checkmarkSprite = FindSprite("checkmark_icon", "checkmarkIcon", "Checkmark", "checkBox_1");
                if (_checkmarkSprite != null) Debug.Log($"[CkQol] checkmark sprite '{_checkmarkSprite.name}'");
                return _checkmarkSprite;
            }
        }

        public static Sprite KnobSprite
        {
            get
            {
                if (_knobSearched) return _knobSprite;
                _knobSearched = true;
                _knobSprite = FindSprite("knob_1", "knob_2", "handle");
                if (_knobSprite != null) Debug.Log($"[CkQol] knob sprite '{_knobSprite.name}'");
                return _knobSprite;
            }
        }

        private static Sprite _cursor;

        /// A pointer drawn on our own canvas.
        ///
        /// The game renders its cursor as UI on its own canvas, so once the menu
        /// sorts above it the real cursor disappears behind the window. Raising the
        /// game's cursor instead would put it above everything, which is worse.
        /// Generated rather than borrowed: the game's cursor is not reliably findable
        /// as a Sprite, and a wrong guess is a visibly wrong pointer.
        public static Sprite CursorSprite
        {
            get
            {
                if (_cursor != null) return _cursor;

                var game = FindSprite("cursor_inv");
                if (game != null)
                {
                    Debug.Log($"[CkQol] cursor sprite '{game.name}'");
                    _cursor = game;
                    return _cursor;
                }

                const int w = 12, h = 19;
                // 0 = transparent, 1 = outline, 2 = fill.
                string[] rows =
                {
                    "1...........",
                    "11..........",
                    "121.........",
                    "1221........",
                    "12221.......",
                    "122221......",
                    "1222221.....",
                    "12222221....",
                    "122222221...",
                    "1222222221..",
                    "12222222221.",
                    "122222111111",
                    "12221221....",
                    "1221.1221...",
                    "121..1221...",
                    "11....1221..",
                    "1......1221.",
                    "........121.",
                    ".........11.",
                };

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                for (int y = 0; y < h; y++)
                {
                    string row = rows[y];
                    for (int x = 0; x < w; x++)
                    {
                        char c = x < row.Length ? row[x] : '.';
                        Color color = c == '1' ? new Color(0.05f, 0.04f, 0.07f, 1f)
                                    : c == '2' ? new Color(0.95f, 0.93f, 0.85f, 1f)
                                    : Color.clear;
                        // Texture origin is bottom-left, the art above reads top-down.
                        tex.SetPixel(x, h - 1 - y, color);
                    }
                }
                tex.Apply();

                // pixelsPerUnit must match Canvas.referencePixelsPerUnit (100) or the
                // sprite's native size is scaled by the ratio between them - at 1 the
                // 12x19 arrow comes out 1200x1900.
                _cursor = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0f, 1f), 100f);
                _cursor.hideFlags = HideFlags.HideAndDontSave;
                return _cursor;
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

        /// Writes every loaded sprite and font to a file. The log truncates and
        /// the game has thousands of sprites, so a file is the only way to get a
        /// usable inventory for picking exact names.
        public static void DumpAssets()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                    "ck-qol-assets.txt");

                var lines = new List<string>();
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                lines.Add($"# TMP fonts ({fonts.Length})");
                foreach (var f in fonts.Where(f => f != null))
                {
                    lines.Add($"font\t{f.name}\tpointSize={f.faceInfo.pointSize}");
                }

                var sprites = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(sp => sp != null && !string.IsNullOrEmpty(sp.name))
                    .OrderBy(sp => sp.name)
                    .ToList();
                lines.Add($"# sprites ({sprites.Count}), sliced ones first are the useful ones");
                foreach (var sp in sprites)
                {
                    bool sliced = sp.border != Vector4.zero;
                    lines.Add($"sprite\t{sp.name}\t{(int)sp.rect.width}x{(int)sp.rect.height}" +
                              $"\tborder={sp.border}\tsliced={sliced}");
                }

                System.IO.File.WriteAllLines(path, lines);
                Debug.Log($"[CkQol] wrote {sprites.Count} sprites and {fonts.Length} fonts to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CkQol] asset dump failed");
                Debug.LogException(e);
            }
        }

        /// The font's own design size. A pixel font only renders cleanly at integer
        /// multiples of this - at 1.4x you get dropped and doubled rows of pixels,
        /// which reads as "corrupted text".
        public static float NativeSize
        {
            get
            {
                var font = Font;
                float size = font != null ? font.faceInfo.pointSize : 0f;
                return size > 0f ? size : 16f;
            }
        }

        /// Rounds a desired size to the nearest whole multiple of the font's native
        /// size, never below 1x.
        public static float Snap(float desired)
        {
            float native = NativeSize;
            if (native <= 0f) return desired;
            return native * Mathf.Max(1f, Mathf.Round(desired / native));
        }

        /// Body text is always exactly 1x native. A pixel font has no legal size
        /// between 1x and 2x, and 2x is far too large for form rows, so every
        /// attempt to be clever here just overflows the layout.
        public static float Body => NativeSize;

        /// Headings only go up a step when the font is small enough to afford it.
        public static float Heading => NativeSize <= 18f ? NativeSize * 2f : NativeSize;

        /// Row height that comfortably fits one line of body text.
        public static float RowHeight => Mathf.Ceil(Body * 1.9f);

        public static void ApplyText(TextMeshProUGUI text, float size, Color color,
                                     TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            if (Font != null) text.font = Font;
            text.fontSize = Snap(size);
            text.color = color;
            text.alignment = align;
            text.richText = true;
            text.raycastTarget = false;
            // Pixel fonts must not be auto-shrunk back off a whole multiple.
            text.enableAutoSizing = false;
        }
    }
}
