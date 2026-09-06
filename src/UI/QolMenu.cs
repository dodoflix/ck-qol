using System.Collections.Generic;
using CkQol.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CkQol.UI
{
    /// The in-game config window: feature tabs down the left, that feature's live
    /// settings on the right. Built once, then shown/hidden - rebuilding the whole
    /// tree on every toggle would drop slider drags and input-field focus.
    public class QolMenu : MonoBehaviour
    {
        private const float Width = 820f;
        private const float Height = 520f;
        private const float TabColumn = 200f;

        private CkQolMod _mod;
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _tabList;
        private RectTransform _pageHost;

        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<GameObject> _pages = new List<GameObject>();
        private int _current = -1;

        public bool IsOpen => _root != null && _root.activeSelf;

        public static QolMenu Create(CkQolMod mod)
        {
            var host = new GameObject("CkQolMenu");
            DontDestroyOnLoad(host);
            var menu = host.AddComponent<QolMenu>();
            menu._mod = mod;
            menu.Build();
            menu.SetOpen(false);
            return menu;
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (_root == null) return;
            _root.SetActive(open);

            if (open)
            {
                RaiseAboveGameUi();
                if (!Cursor.visible)
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                }
            }
        }

        /// Core Keeper draws its own cursor as a UI element, so a fixed sortingOrder
        /// is a guess that loses whenever the game's cursor canvas sits higher.
        /// Measured on open instead - the game can add canvases at any time.
        private void RaiseAboveGameUi()
        {
            if (_canvas == null) return;

            // Count every live canvas, not just root ones: game UI is usually built
            // from nested canvases with overrideSorting, and those carry the high
            // sorting orders. Filtering to isRootCanvas measured the wrong ones and
            // left the menu underneath the HUD and the game's custom cursor.
            int highest = 0;
            string top = "(none)";
            foreach (var other in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (other == null || other == _canvas) continue;
                // Skip prefab assets - only things actually in a scene can draw over us.
                if (!other.gameObject.scene.IsValid()) continue;
                if (other.sortingOrder > highest)
                {
                    highest = other.sortingOrder;
                    top = other.name;
                }
            }

            int wanted = Mathf.Min(highest + 100, short.MaxValue);
            _canvas.overrideSorting = true;
            if (_canvas.sortingOrder != wanted)
            {
                _canvas.sortingOrder = wanted;
                Debug.Log($"[CkQol] menu sortingOrder={wanted} (highest game canvas '{top}'={highest})");
            }

            if (highest >= short.MaxValue)
            {
                // Nothing left to outrank it with; ties fall back to hierarchy order,
                // which we do not control. Worth saying out loud rather than silently
                // rendering underneath.
                Debug.LogWarning($"[CkQol] game canvas '{top}' is already at the maximum " +
                                 "sorting order, the menu may draw underneath it");
            }
        }

        private void Build()
        {
            UiFactory.EnsureEventSystem();

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the game's own HUD, below nothing we care about.
            _canvas.sortingOrder = 32000;

            // Constant pixel size with a whole-number factor. ScaleWithScreenSize
            // produces a fractional scale on any resolution that is not exactly the
            // reference, and a fractional scale re-corrupts the pixel font no matter
            // how carefully its point size was snapped.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = _mod.UiScaleFactor;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Everything lives under one root so showing/hiding is a single SetActive.
            var rootGo = UiFactory.Node("Root", canvasGo.transform, out RectTransform rootRect);
            UiFactory.Stretch(rootRect, 0f, 0f);
            _root = rootGo;

            // Full-screen catcher: absorbs clicks so they cannot reach the game behind,
            // dims the scene, and closes the menu when clicked outside the window.
            // Added before the window so it renders underneath it.
            var blockerGo = UiFactory.Node("Blocker", rootGo.transform, out RectTransform blockerRect);
            UiFactory.Stretch(blockerRect, 0f, 0f);
            var blockerImage = blockerGo.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.45f);
            blockerImage.raycastTarget = true;
            var blockerButton = blockerGo.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(() => SetOpen(false));

            // Window
            var window = UiFactory.Panel("Window", rootGo.transform, GameTheme.PanelBg,
                                         out RectTransform windowRect);
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);

            // Sized from the font, not hardcoded: with a large native font size a
            // fixed 820x520 window cannot fit its own rows. Capped to the screen so
            // it never grows off-display at high UI scale.
            float scale = Mathf.Max(1f, _mod.UiScaleFactor);
            float wanted = Mathf.Max(Width, GameTheme.Body * 34f);
            float wantedHeight = Mathf.Max(Height, GameTheme.RowHeight * 13f);
            windowRect.sizeDelta = new Vector2(
                Mathf.Min(wanted, Screen.width / scale - 40f),
                Mathf.Min(wantedHeight, Screen.height / scale - 40f));

            var outline = window.gameObject.AddComponent<Outline>();
            outline.effectColor = GameTheme.PanelBorder;
            outline.effectDistance = new Vector2(2f, -2f);

            // Title bar
            var title = UiFactory.Label("Title", window.transform,
                                        "Core Keeper QoL", GameTheme.Heading, GameTheme.Accent);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 36f);
            titleRect.anchoredPosition = new Vector2(0f, -6f);
            titleRect.offsetMin = new Vector2(16f, titleRect.offsetMin.y);

            var hint = UiFactory.Label("Hint", window.transform,
                                       "changes apply immediately", GameTheme.Body, GameTheme.TextDim,
                                       TextAlignmentOptions.MidlineRight);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = new Vector2(0f, 36f);
            hintRect.anchoredPosition = new Vector2(0f, -8f);
            hintRect.offsetMax = new Vector2(-16f, hintRect.offsetMax.y);

            // Tab column
            var tabPanel = UiFactory.Panel("Tabs", window.transform, GameTheme.TabIdle * 0.6f,
                                           out RectTransform tabPanelRect);
            tabPanelRect.anchorMin = new Vector2(0f, 0f);
            tabPanelRect.anchorMax = new Vector2(0f, 1f);
            tabPanelRect.pivot = new Vector2(0f, 0.5f);
            tabPanelRect.sizeDelta = new Vector2(TabColumn, -52f);
            tabPanelRect.anchoredPosition = new Vector2(10f, -10f);

            var tabScroll = MakeScroll(tabPanel.transform, out _tabList);

            // Page host
            var pagePanel = UiFactory.Panel("Pages", window.transform, new Color(0f, 0f, 0f, 0.16f),
                                            out RectTransform pagePanelRect);
            pagePanelRect.anchorMin = new Vector2(0f, 0f);
            pagePanelRect.anchorMax = new Vector2(1f, 1f);
            pagePanelRect.offsetMin = new Vector2(TabColumn + 20f, 10f);
            pagePanelRect.offsetMax = new Vector2(-10f, -52f);
            _pageHost = pagePanelRect;

            BuildTabs();
            if (_tabButtons.Count > 0) Select(0);
        }

        private static ScrollRect MakeScroll(Transform parent, out RectTransform content)
        {
            var scrollGo = UiFactory.Node("Scroll", parent, out RectTransform scrollRect);
            UiFactory.Stretch(scrollRect, 6f, 6f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewportGo = UiFactory.Node("Viewport", scrollGo.transform, out RectTransform viewportRect);
            UiFactory.Stretch(viewportRect, 0f, 0f);
            viewportGo.AddComponent<RectMask2D>();
            scroll.viewport = viewportRect;

            UiFactory.Node("Content", viewportGo.transform, out content);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // A new RectTransform starts at sizeDelta (100,100). Changing the anchors
            // does not reset it, so a top-stretched content ends up parent-width +100,
            // overhanging 50px each side and getting clipped by the mask.
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return scroll;
        }

        private void BuildTabs()
        {
            foreach (var feature in _mod.Features)
            {
                int index = _pages.Count;
                var button = UiFactory.FlatButton("Tab_" + feature.Name, _tabList,
                                                  feature.Name, GameTheme.RowHeight, () => Select(index));
                _tabButtons.Add(button);
                _pages.Add(BuildPage(feature));
            }
        }

        private GameObject BuildPage(FeatureHandle feature)
        {
            var pageGo = UiFactory.Node("Page_" + feature.Name, _pageHost, out RectTransform pageRect);
            UiFactory.Stretch(pageRect, 0f, 0f);
            MakeScroll(pageGo.transform, out RectTransform content);

            var header = UiFactory.Label("Desc", content, feature.Description, GameTheme.Body, GameTheme.TextDim);
            header.textWrappingMode = TextWrappingModes.Normal;
            var headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.minHeight = GameTheme.RowHeight * 1.2f;

            // Enable toggle, unless this is a pseudo-feature with nothing to switch off.
            if (feature.CanBeDisabled)
            {
                UiFactory.Row(content, "Enabled", "Turn this feature on or off", GameTheme.RowHeight,
                              out RectTransform slot);
                UiFactory.Checkbox(slot, feature.Enabled.Value,
                                   v => feature.Enabled.Value = v);
            }

            foreach (var setting in feature.Settings)
            {
                AddSettingRow(content, setting);
            }

            pageGo.SetActive(false);
            return pageGo;
        }

        private static void AddSettingRow(Transform content, ModSetting setting)
        {
            switch (setting)
            {
                case BoolSetting b:
                {
                    UiFactory.Row(content, b.Label, b.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    UiFactory.Checkbox(slot, b.Value, v => b.Value = v);
                    break;
                }
                case IntSetting i:
                {
                    UiFactory.Row(content, i.Label, i.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    UiFactory.HorizontalSlider(slot, i.Value, i.Min, i.Max, true,
                                               v => i.Value = Mathf.RoundToInt(v), out _);
                    break;
                }
                case FloatSetting f:
                {
                    UiFactory.Row(content, f.Label, f.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    UiFactory.HorizontalSlider(slot, f.Value, f.Min, f.Max, false,
                                               v => f.Value = v, out _);
                    break;
                }
                case ChoiceSetting c:
                {
                    UiFactory.Row(content, c.Label, c.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    BuildChoice(slot, c);
                    break;
                }
                case KeySetting k:
                {
                    UiFactory.Row(content, k.Label, k.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    UiFactory.TextBox(slot, k.Value.ToString(), v =>
                    {
                        if (System.Enum.TryParse(v, true, out KeyCode parsed)) k.Value = parsed;
                    });
                    break;
                }
                case StringSetting s:
                {
                    UiFactory.Row(content, s.Label, s.Tooltip, GameTheme.RowHeight, out RectTransform slot);
                    UiFactory.TextBox(slot, s.Value, v => s.Value = v);
                    break;
                }
            }
        }

        private static void BuildChoice(Transform slot, ChoiceSetting setting)
        {
            var rowGo = UiFactory.Node("Choices", slot, out RectTransform rowRect);
            UiFactory.Stretch(rowRect, 0f, 0f);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var buttons = new List<Image>();
            for (int i = 0; i < setting.Options.Length; i++)
            {
                string option = setting.Options[i];
                var button = UiFactory.FlatButton("Opt_" + option, rowGo.transform, option, GameTheme.RowHeight * 0.8f, null);
                var image = button.targetGraphic as Image;
                buttons.Add(image);
                button.onClick.AddListener(() =>
                {
                    setting.Value = option;
                    for (int j = 0; j < buttons.Count; j++)
                    {
                        buttons[j].color = setting.Options[j] == setting.Value
                            ? GameTheme.TabActive : GameTheme.TabIdle;
                    }
                });
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].color = i == setting.Index ? GameTheme.TabActive : GameTheme.TabIdle;
            }
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _current = index;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].SetActive(i == index);
                if (_tabButtons[i].targetGraphic is Image image)
                {
                    image.color = i == index ? GameTheme.TabActive : GameTheme.TabIdle;
                }
            }
        }
    }
}
