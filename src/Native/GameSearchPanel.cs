using System;
using System.Collections.Generic;
using UnityEngine;

namespace CkQol.Native
{
    /// One line of the panel: an icon, a label, and a number on the right.
    public struct SearchRow
    {
        public Sprite Icon;
        public string Text;
        public string Amount;
    }

    /// A search box with name suggestions and a result list, drawn beside the
    /// inventory.
    ///
    /// Typing rides the game's own pipeline: registering as
    /// Manager.input.activeInputField makes MenuManager.HandleTypingInput feed this
    /// Input.inputString, and that runs before any menu check, so it works in the
    /// world and not only in a menu.
    public class CkQolSearchPanel : MonoBehaviour, InputManager.TextInputInterface
    {
        public Func<bool> Visible;
        public Func<string, List<SearchRow>> Suggest;
        public Action<int> Picked;
        public Action Cleared;

        /// 15px, the step the HUD stacks its own widgets by.
        private const float RowStep = 0.9375f;

        /// Clear of the inventory window's own frame.
        private const float FromInventory = 0.6f;

        private const int MaxRows = 10;
        private const int MaxLength = 32;

        /// Row index sentinels for the parts that are not suggestions.
        internal const int FocusTarget = -1;
        internal const int ClearTarget = -2;

        private class Row
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public PugText Text;
            public PugText Amount;
            public PugText[] AmountShadows;
            public CkQolSearchPick Pick;
            public BoxCollider Box;
            public string Shown;
        }

        private HoverRequiredMaterialUIElement _donor;
        private Transform _anchor;

        private PugText _query;
        private PugText _hint;
        private PugText _clear;
        private SpriteRenderer _listPanel;
        private SpriteRenderer _window;

        private readonly List<Row> _pool = new List<Row>();
        private readonly List<SearchRow> _suggestions = new List<SearchRow>();
        private readonly List<SearchRow> _results = new List<SearchRow>();

        private string _typed = string.Empty;
        private int _caret;
        private int _highlight;
        private bool _focused;
        private bool _active;
        private string _title;
        private string _shownQuery;

        /// Stamped by any of our own clickable parts, so a click anywhere else can
        /// be told apart from one on the panel.
        internal int ClickedFrame;

        internal void Bind(HoverRequiredMaterialUIElement donor, Transform anchor,
                           PugText query, PugText hint, PugText clear,
                           SpriteRenderer listPanel, SpriteRenderer window)
        {
            _donor = donor;
            _anchor = anchor;
            _query = query;
            _hint = hint;
            _clear = clear;
            _listPanel = listPanel;
            _window = window;
        }

        /// Handed the current results by the feature.
        public void SetRows(string title, List<SearchRow> rows)
        {
            _title = title;
            _results.Clear();
            if (rows != null) _results.AddRange(rows);
        }

        /// Takes the keyboard, and only that.
        ///
        /// No DisableInput, which the chat and sign fields pair with this: it stops
        /// every player key and with them all UI hovering and clicking, so slots and
        /// suggestions go dead while typing. It is not needed here either, because
        /// PlayerController.isMovingBlocked (:735-743) already returns true whenever
        /// an inventory is showing, and this panel only exists then.
        internal void Focus()
        {
            if (_focused) return;

            _focused = true;
            _caret = _typed.Length;
            Manager.input.SetActiveInputField(this);
        }

        private void Blur()
        {
            if (!_focused) return;

            _focused = false;
            if (Manager.input.activeInputField == (InputManager.TextInputInterface)this)
            {
                Manager.input.SetActiveInputField(null);
            }
        }

        internal void ClearQuery()
        {
            _typed = string.Empty;
            _caret = 0;
            _suggestions.Clear();
            _highlight = 0;
            Cleared?.Invoke();
        }

        private void Update()
        {
            bool visible = Visible != null && Visible();
            if (!visible)
            {
                if (_focused) Blur();
                return;
            }

            if (!_focused) return;

            // Up and down are not part of the typing pipeline - MenuManager only
            // forwards left and right, as caret movement - so they are polled.
            if (_suggestions.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.DownArrow)) Step(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) Step(-1);
            }
        }

        private void Step(int by)
        {
            _highlight = (_highlight + by + _suggestions.Count) % _suggestions.Count;
        }

        private void LateUpdate()
        {
            bool visible = Visible != null && Visible();
            if (visible != _active) Show(visible);
            if (!visible) return;

            transform.localScale = Manager.ui.CalcGameplayUITargetScaleMultiplier();
            Place();

            // The game only auto-deactivates a real TextInputField on an outside
            // click (UIMouse.cs:593-595), and this is deliberately not one. Checked
            // in LateUpdate, so a click UIMouse handled in Update has already
            // stamped ClickedFrame.
            if (_focused && Input.GetMouseButtonDown(0) && ClickedFrame != Time.frameCount)
            {
                Blur();
            }

            DrawQuery();

            int used = DrawRows();
            for (int i = used; i < _pool.Count; i++) Blank(_pool[i]);

            Frame(used);
        }

        private void DrawQuery()
        {
            string shown = _typed + (_focused ? "_" : string.Empty);
            if (shown != _shownQuery)
            {
                GameMenu.SetLiteral(_query, shown);
                _shownQuery = shown;
            }

            // The donor's hint reads "Label...", and nothing hides it any more: the
            // script that did was destroyed with the rest of the chest field.
            if (_hint != null) _hint.gameObject.SetActive(_typed.Length == 0);
            if (_clear != null) _clear.gameObject.SetActive(_typed.Length > 0);
        }

        private int DrawRows()
        {
            int used = 0;

            if (_suggestions.Count > 0)
            {
                for (int i = 0; i < _suggestions.Count && used < MaxRows; i++, used++)
                {
                    var row = RowAt(used);
                    if (row == null) break;

                    row.Pick.Index = i;
                    Draw(row, used, _suggestions[i].Icon, _suggestions[i].Text,
                         string.Empty, i == _highlight);
                }
                return used;
            }

            if (_title != null && used < MaxRows)
            {
                var head = RowAt(used);
                if (head != null)
                {
                    head.Pick.Index = FocusTarget;
                    Draw(head, used, null, _title, string.Empty, false);
                    used++;
                }
            }

            for (int i = 0; i < _results.Count && used < MaxRows; i++, used++)
            {
                var row = RowAt(used);
                if (row == null) break;

                row.Pick.Index = FocusTarget;
                Draw(row, used, _results[i].Icon, _results[i].Text, _results[i].Amount, false);
            }
            return used;
        }

        /// Beside the inventory, following it rather than a screen corner - the HUD
        /// has no anchoring of its own, every widget carries a position.
        ///
        /// Measured off the window's own background in world space rather than in
        /// its local units: that renderer already carries the window's width and the
        /// UI scale, and a bag upgrade widens it.
        private void Place()
        {
            if (_anchor == null) return;

            Vector3 at = _anchor.position;
            float edge = _window != null ? _window.bounds.max.x : at.x;

            transform.position = new Vector3(edge + FromInventory * transform.localScale.x,
                                             at.y, at.z);
        }

        /// The backing panel, grown to whatever is showing.
        private void Frame(int rows)
        {
            if (_listPanel == null) return;

            _listPanel.enabled = rows > 0;
            if (rows == 0) return;

            float height = rows * RowStep + 0.5f;
            _listPanel.size = new Vector2(_listPanel.size.x, height);
            _listPanel.transform.localPosition = new Vector3(
                _listPanel.transform.localPosition.x,
                RowStep * 0.5f - height / 2f,
                _listPanel.transform.localPosition.z);
        }

        private void Draw(Row row, int index, Sprite icon, string text, string amount,
                          bool highlighted)
        {
            row.Root.transform.localPosition = new Vector3(0f, -RowStep * index, 0f);

            string shown = text + " " + amount;
            if (row.Shown != shown)
            {
                GameMenu.SetLiteral(row.Text, text);
                GameMenu.SetLiteral(row.Amount, amount);
                foreach (var shadow in row.AmountShadows) GameMenu.SetLiteral(shadow, amount);
                row.Shown = shown;
            }

            row.Icon.sprite = icon;
            row.Icon.enabled = icon != null;

            // Recoloured every frame: Render rebuilds the glyphs from the style and
            // loses any colour put on them.
            Recolour(row.Text, highlighted || row.Pick.Hovered
                ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR
                : Color.white);

            // Sized from the text actually drawn, the way QolStepStrip places its
            // boxes: a guessed collider misses the row at some UI scales.
            float width = Mathf.Max(row.Text.dimensions.width, 4f);
            row.Box.size = new Vector3(width, RowStep, 0.4f);
            row.Box.center = new Vector3(width * 0.5f, 0f, 0f);
        }

        private static void Recolour(PugText text, Color tint)
        {
            if (text == null) return;

            var glyphs = text.glyphs;
            for (int i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i] != null) glyphs[i].color = tint;
            }
        }

        private void Blank(Row row)
        {
            if (row == null || row.Shown == string.Empty) return;

            GameMenu.SetLiteral(row.Text, string.Empty);
            GameMenu.SetLiteral(row.Amount, string.Empty);
            foreach (var shadow in row.AmountShadows) GameMenu.SetLiteral(shadow, string.Empty);

            row.Icon.enabled = false;
            row.Pick.Index = FocusTarget;
            row.Box.size = Vector3.zero;
            row.Shown = string.Empty;
        }

        private void Show(bool visible)
        {
            // PugText releases its glyphs to the pool when disabled, so everything
            // has to be rendered again on the way back in.
            foreach (var row in _pool) row.Shown = null;
            _shownQuery = null;

            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(visible);
            }

            if (!visible) Blur();
            _active = visible;
        }

        private Row RowAt(int index)
        {
            while (_pool.Count <= index)
            {
                var row = Build();
                if (row == null) return null;
                _pool.Add(row);
            }
            return _pool[index];
        }

        private Row Build()
        {
            var clone = UnityEngine.Object.Instantiate(_donor.gameObject, GameMenu.Staging);
            clone.name = "CkQolSearchRow";

            var element = clone.GetComponent<HoverRequiredMaterialUIElement>();
            if (element == null || element.text == null || element.SR == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                Debug.LogError("[CkQol] search row donor is missing its icon or text");
                return null;
            }

            foreach (var stale in clone.GetComponentsInChildren<PlatformDependentPugText>(true))
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }

            foreach (var text in clone.GetComponentsInChildren<PugText>(true))
            {
                text.maxWidth = 0f;
            }

            var amountShadows = new List<PugText>();
            if (element.amountNumberShadow != null) amountShadows.Add(element.amountNumberShadow);
            if (element.amountNumberShadow2 != null) amountShadows.Add(element.amountNumberShadow2);

            Silence(element.chestAmountNumber);
            Silence(element.chestAmountNumberShadow);
            Silence(element.chestAmountNumberShadow2);
            if (element.chestIcon != null) element.chestIcon.enabled = false;

            if (element.container != null)
            {
                element.container.transform.localPosition = Vector3.zero;
                element.container.SetActive(true);
            }

            element.SR.color = Color.white;

            // The click raycast is masked to the UI layer and takes the UIelement off
            // whatever collider it hits, so both have to sit on the row's root.
            clone.layer = _anchor.gameObject.layer;

            var pick = clone.AddComponent<CkQolSearchPick>();
            pick.Panel = this;
            pick.Index = FocusTarget;

            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;

            clone.transform.SetParent(transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.SetActive(true);

            return new Row
            {
                Root = clone,
                Icon = element.SR,
                Text = element.text,
                Amount = element.amountNumber,
                AmountShadows = amountShadows.ToArray(),
                Pick = pick,
                Box = box,
            };
        }

        private static void Silence(PugText text)
        {
            if (text != null) GameMenu.SetLiteral(text, string.Empty);
        }

        internal void Choose(int index)
        {
            if (index < 0 || index >= _suggestions.Count) return;

            _highlight = index;
            Commit();
        }

        private void Commit()
        {
            if (_suggestions.Count == 0) return;

            int index = Mathf.Clamp(_highlight, 0, _suggestions.Count - 1);

            // The box shows what was picked rather than the fragment that found it,
            // so it is clear afterwards what is being listed.
            _typed = Trim(_suggestions[index].Text);
            _caret = _typed.Length;

            _suggestions.Clear();
            Picked?.Invoke(index);
        }

        private void Refresh()
        {
            _suggestions.Clear();
            _highlight = 0;

            if (Suggest == null) return;
            var found = Suggest(_typed);
            if (found != null) _suggestions.AddRange(found);
        }

        // InputManager.TextInputInterface. Lifted from QolTextOption, which already
        // implements this for the settings menu.

        public bool WasAutoActivated { get; set; }

        public int MaxCharactersForOnScreenKeyboard => MaxLength;

        public string GetInputText() => _typed;

        public void SetInputText(string input)
        {
            _typed = Trim(input);
            _caret = _typed.Length;
            Refresh();
        }

        public void AppendString(string input)
        {
            if (string.IsNullOrEmpty(input)) return;

            // inputString carries backspace and CR; MenuManager handles those.
            var text = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= ' ' && c != '\u007f') text.Append(c);
            }
            if (text.Length == 0) return;

            _caret = Mathf.Clamp(_caret, 0, _typed.Length);
            _typed = Trim(_typed.Insert(_caret, text.ToString()));
            _caret = Mathf.Min(_caret + text.Length, _typed.Length);
            Refresh();
        }

        public void MoveCharMarker(int relativeChange)
        {
            _caret = Mathf.Clamp(_caret + relativeChange, 0, _typed.Length);
        }

        public void RemoveCharAtMarker()
        {
            if (_caret >= _typed.Length) return;
            _typed = _typed.Remove(_caret, 1);
            Refresh();
        }

        public void RemoveCharBehindMarker()
        {
            if (_caret <= 0) return;
            _typed = _typed.Remove(_caret - 1, 1);
            _caret--;
            Refresh();
        }

        public void Deactivate(bool commit)
        {
            if (commit && _suggestions.Count > 0) Commit();
            else if (!commit) ClearQuery();

            Blur();
        }

        public string GetHintString() => "search";

        public bool IsHidden() => false;

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
        }
    }

    /// A clickable part of the panel. Same mechanism as QolStepStrip: a collider
    /// plus a ButtonUIElement subclass, because a prefab's persistent call cannot be
    /// re-pointed at runtime.
    public class CkQolSearchPick : ButtonUIElement
    {
        public CkQolSearchPanel Panel;
        public int Index = CkQolSearchPanel.FocusTarget;

        internal bool Hovered;

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
            Hovered = true;
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            Hovered = false;
        }

        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            base.OnLeftClicked(mod1, mod2);
            if (Panel == null) return;

            Panel.ClickedFrame = Time.frameCount;

            if (Index >= 0) Panel.Choose(Index);
            else if (Index == CkQolSearchPanel.ClearTarget) Panel.ClearQuery();
            else Panel.Focus();
        }
    }

    public static class GameSearchPanel
    {
        /// Returns null while the HUD does not exist yet, so callers should keep
        /// trying.
        public static CkQolSearchPanel Install()
        {
            var ui = Manager.ui;
            if (ui == null || ui.playerInventoryUI == null) return null;

            var mouse = ui.mouse;
            if (mouse == null || mouse.hoverMaterials == null) return null;

            HoverRequiredMaterialUIElement donor = null;
            foreach (var candidate in mouse.hoverMaterials)
            {
                if (candidate == null) continue;
                donor = candidate;
                break;
            }
            if (donor == null) return null;

            var field = ui.chestInventoryUI != null ? ui.chestInventoryUI.inputField : null;
            if (field == null || field.pugText == null) return null;

            try
            {
                var anchor = ui.playerInventoryUI.transform;
                int layer = anchor.gameObject.layer;

                var root = new GameObject("CkQolSearchPanel");
                root.layer = layer;
                root.transform.SetParent(anchor.parent, false);

                var panel = root.AddComponent<CkQolSearchPanel>();

                // Behind everything else, so it is added first.
                var listPanel = Backing(root.transform, ui, new Vector3(3f, 0f, 0.2f), 8f, 1f);
                Backing(root.transform, ui, new Vector3(3f, 1.6f, 0.2f), 8f, 1.3f);

                // The donor's own script implements this same interface and would
                // fight for the active field. Destroying it also takes
                // onInputFieldDone with it, whose prefab listener renames whatever
                // chest the player has open.
                var box = UnityEngine.Object.Instantiate(field.gameObject, GameMenu.Staging);
                box.name = "CkQolSearchBox";

                // Read off the clone's own script before destroying it: it names the
                // two texts and the caret, which are otherwise indistinguishable
                // among the children.
                var wiring = box.GetComponent<TextInputField>();
                PugText query = wiring != null ? wiring.pugText : null;
                PugText hint = wiring != null ? wiring.hintText : null;
                GameObject marker = wiring != null ? wiring.selectedMarker : null;
                var blinker = wiring != null ? wiring.characterMarkBlinker : null;

                // The caret is a sprite the blinker drove. Destroying only the script
                // leaves it lit and parked over the text.
                if (marker != null) marker.SetActive(false);
                if (blinker != null) blinker.gameObject.SetActive(false);

                foreach (var stale in box.GetComponentsInChildren<TextInputField>(true))
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }
                foreach (var stale in box.GetComponentsInChildren<CharacterMarkBlinker>(true))
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                }
                foreach (var text in box.GetComponentsInChildren<PugText>(true))
                {
                    text.maxWidth = 0f;
                }

                if (query == null)
                {
                    UnityEngine.Object.DestroyImmediate(box);
                    Debug.LogError("[CkQol] search box donor has no text to type into");
                    return null;
                }

                if (hint != null)
                {
                    hint.localize = false;
                    GameMenu.SetLiteral(hint, "search...");
                }

                box.layer = layer;
                var focus = box.AddComponent<CkQolSearchPick>();
                focus.Panel = panel;
                focus.Index = CkQolSearchPanel.FocusTarget;

                var collider = box.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(7f, 1f, 0.4f);
                collider.center = new Vector3(3f, 0f, 0f);

                box.transform.SetParent(root.transform, false);
                box.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                box.SetActive(true);

                var clear = Clear(root.transform, layer, panel, query,
                                  new Vector3(7.4f, 1.6f, 0f));

                panel.Bind(donor, anchor, query, hint, clear, listPanel,
                           ui.playerInventoryUI.backgroundSR);

                Debug.Log("[CkQol] added the search panel to the HUD");
                return panel;
            }
            catch (Exception e)
            {
                Debug.LogError("[CkQol] failed to add the search panel");
                Debug.LogException(e);
                return null;
            }
        }

        /// A backing panel cloned from the inventory window's own, which is a sliced
        /// sprite and so takes any size.
        private static SpriteRenderer Backing(Transform parent, UIManager ui,
                                              Vector3 at, float width, float height)
        {
            var donor = ui.playerInventoryUI.backgroundSR;
            if (donor == null) return null;

            var clone = UnityEngine.Object.Instantiate(donor.gameObject, GameMenu.Staging);
            clone.name = "CkQolSearchBacking";

            var sr = clone.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                return null;
            }

            sr.color = donor.color;
            sr.size = new Vector2(width, height);

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = at;
            clone.SetActive(true);
            return sr;
        }

        /// The clear button: the query's own text object cloned, so it matches, with
        /// a collider over it.
        private static PugText Clear(Transform parent, int layer, CkQolSearchPanel panel,
                                     PugText style, Vector3 at)
        {
            if (style == null) return null;

            var clone = UnityEngine.Object.Instantiate(style.gameObject, GameMenu.Staging);
            clone.name = "CkQolSearchClear";
            clone.layer = layer;

            var pug = clone.GetComponent<PugText>();
            pug.maxWidth = 0f;
            GameMenu.SetLiteral(pug, "[x]");

            var pick = clone.AddComponent<CkQolSearchPick>();
            pick.Panel = panel;
            pick.Index = CkQolSearchPanel.ClearTarget;

            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.2f, 1f, 0.4f);
            box.center = new Vector3(0.5f, 0f, 0f);

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = at;
            clone.SetActive(true);
            return pug;
        }
    }
}
