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

        /// Greyed: a suggestion for something no container in range holds.
        public bool Dim;
    }

    /// A search box with name suggestions and a result list, drawn beside the
    /// inventory.
    ///
    /// Typing rides the game's own pipeline: registering as
    /// Manager.input.activeInputField makes MenuManager.HandleTypingInput feed this
    /// Input.inputString, and that runs before any menu check, so it works in the
    /// world and not only in a menu.
    /// A UIelement, not a plain MonoBehaviour, and that is load bearing:
    /// UIMouse.TrySelectNewElement (:837) casts Manager.input.activeInputField to
    /// UIelement unconditionally. A field that is not one throws there every frame
    /// the mouse moves, taking the whole of UIMouse's update with it - which reads
    /// as hovering having died everywhere, not as an error.
    public class CkQolSearchPanel : UIelement, InputManager.TextInputInterface
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
        private const int MaxLength = 24;

        /// What fits a row between the icon and the count column.
        private const int RowCharacters = 8;

        /// The box has the icon and the clear button beside it.
        private const int InputCharacters = 8;

        /// Characters a second a row scrolls by when its name is too long to show at
        /// once.
        private const float MarqueeRate = 3.5f;

        /// Wide enough for RowCharacters plus an icon, and no wider - the panel sits
        /// against the right of the screen.
        internal const float PanelWidth = 6f;

        /// How far the box sits above the first row. Tight, so the two read as one
        /// panel rather than as two.
        internal const float BoxRow = 1.15f;

        /// Where the count ends, short of the backing's right edge.
        private const float CountGap = 0.75f;

        private class Row
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public PugText Text;
            public PugText Amount;
            public BoxCollider Box;
            public int Index;
        }

        private HoverRequiredMaterialUIElement _donor;
        private Transform _anchor;

        /// Where the whole panel draws, taken from the search box's own text. Its
        /// donor sits under the item tooltip; the rows come from the hover window,
        /// whose donor sits over it, so they have to be brought down to match or the
        /// tooltip covers half the panel.
        private int _sortLayer;
        private int _sortOrder;

        private PugText _query;
        private PugText _hint;
        private PugText _clear;
        private SpriteRenderer _listPanel;
        private SpriteRenderer _window;
        private SpriteRenderer _picked;
        private BoxCollider _boxHit;
        private BoxCollider _clearHit;

        private readonly List<Row> _pool = new List<Row>();
        private readonly List<SearchRow> _suggestions = new List<SearchRow>();
        private readonly List<SearchRow> _results = new List<SearchRow>();

        private string _typed = string.Empty;
        private int _caret;
        private int _highlight;
        private bool _focused;
        private bool _active;
        private int _hovered = -1;

        internal void Bind(HoverRequiredMaterialUIElement donor, Transform anchor,
                           PugText query, PugText hint, PugText clear,
                           SpriteRenderer listPanel, SpriteRenderer window,
                           SpriteRenderer picked, BoxCollider boxHit, BoxCollider clearHit)
        {
            _donor = donor;
            _anchor = anchor;
            _query = query;
            _hint = hint;
            _clear = clear;
            _listPanel = listPanel;
            _window = window;
            _picked = picked;
            _boxHit = boxHit;
            _clearHit = clearHit;

            if (query != null)
            {
                _sortLayer = query.style.sortingLayer;
                _sortOrder = query.style.orderInLayer;
            }

            Sort(picked);
            Sort(clear);
        }

        /// Brings a part onto the panel's own draw order.
        private void Sort(PugText text)
        {
            if (text == null) return;

            text.style.sortingLayer = _sortLayer;
            text.style.orderInLayer = _sortOrder;
        }

        private void Sort(SpriteRenderer sprite)
        {
            if (sprite == null) return;

            sprite.sortingLayerID = _sortLayer;
            sprite.sortingOrder = _sortOrder;
        }

        /// Handed the current results by the feature.
        public void SetRows(List<SearchRow> rows)
        {
            _results.Clear();
            if (rows != null) _results.AddRange(rows);
        }

        /// Takes the keyboard, and stops the player's keys reaching the game.
        ///
        /// DisableInput is needed despite the inventory already blocking movement:
        /// the inventory shortcuts are gated only on the window being open
        /// (PlayerController.cs:1685-1698), so without it typing an f locks a slot
        /// and a q quick stacks. It costs clicking, which UIMouse drives from
        /// UI_INTERACT - so the panel hit tests its own rows instead.
        internal void Focus()
        {
            if (_focused) return;

            _focused = true;
            _caret = _typed.Length;
            Manager.input.SetActiveInputField(this);
            Manager.input.DisableInput();
        }

        private void Blur()
        {
            if (!_focused) return;

            _focused = false;
            if (Manager.input.activeInputField == (InputManager.TextInputInterface)this)
            {
                Manager.input.SetActiveInputField(null);
            }
            Manager.input.EnableInput();
        }

        internal void ClearQuery()
        {
            _typed = string.Empty;
            _caret = 0;
            _suggestions.Clear();
            _highlight = 0;
            if (_picked != null) _picked.enabled = false;
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

        /// The suggestion the pointer is over, or -1. Also used for the hover tint.
        ///
        /// Hit tested here rather than through UIMouse, which would need our parts to
        /// be UIelements with colliders. Being selectable at all was enough to leave
        /// the crafting hover's material rows stuck on screen, and clicks would not
        /// arrive anyway: UIMouse drives them from UI_INTERACT, which is off while
        /// typing.
        private int Over()
        {
            var pointer = Manager.ui.mouse != null ? Manager.ui.mouse.pointer : null;
            if (pointer == null) return -1;

            Vector3 at = pointer.position;
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].Index >= 0 && Covers(_pool[i].Box, at)) return _pool[i].Index;
            }
            return -1;
        }

        private bool OverClear()
        {
            var pointer = Manager.ui.mouse != null ? Manager.ui.mouse.pointer : null;
            if (pointer == null || _clear == null || !_clear.gameObject.activeSelf) return false;

            return Covers(_clearHit, pointer.position);
        }

        /// Acts on whichever of our own parts the pointer is over, and says whether
        /// it found one.
        private bool Hit()
        {
            var pointer = Manager.ui.mouse != null ? Manager.ui.mouse.pointer : null;
            if (pointer == null) return false;

            Vector3 at = pointer.position;

            if (OverClear())
            {
                ClearQuery();
                return true;
            }

            int over = Over();
            if (over >= 0)
            {
                Choose(over);
                return true;
            }

            if (_boxHit != null && Covers(_boxHit, at))
            {
                Focus();
                return true;
            }

            return false;
        }

        private static bool Covers(BoxCollider box, Vector3 at)
        {
            if (box == null || box.size == Vector3.zero) return false;

            Bounds bounds = box.bounds;
            return at.x >= bounds.min.x && at.x <= bounds.max.x &&
                   at.y >= bounds.min.y && at.y <= bounds.max.y;
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();

            bool visible = Visible != null && Visible();
            if (visible != _active) Show(visible);
            if (!visible) return;

            transform.localScale = Manager.ui.CalcGameplayUITargetScaleMultiplier();
            Place();

            // Closing on a click elsewhere is ours to do too: the game does that only
            // for a real TextInputField (UIMouse.cs:593-595).
            if (Input.GetMouseButtonDown(0) && !Hit() && _focused)
            {
                Blur();
            }

            _hovered = Over();

            DrawQuery();

            int used = DrawRows();
            for (int i = used; i < _pool.Count; i++) Blank(_pool[i]);

            Frame(used);
        }

        private void DrawQuery()
        {
            // Focused, the window follows the caret, so typing always shows what is
            // being typed. Left alone it scrolls, the same as a row, because then the
            // whole name is worth reading rather than the end of it.
            string typed = _typed;
            if (typed.Length > InputCharacters)
            {
                if (_focused)
                {
                    int start = Mathf.Clamp(_caret - InputCharacters, 0,
                                            typed.Length - InputCharacters);
                    typed = typed.Substring(start, InputCharacters);
                }
                else
                {
                    typed = Marquee(typed, 0, InputCharacters);
                }
            }

            Write(_query, typed + (_focused ? "_" : string.Empty));

            // The donor's hint reads "Label...", and nothing hides it any more: the
            // script that did was destroyed with the rest of the chest field.
            if (_hint != null) _hint.gameObject.SetActive(_typed.Length == 0);

            if (_clear != null)
            {
                _clear.gameObject.SetActive(_typed.Length > 0);
                GameMenu.Tint(_clear, OverClear()
                    ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR
                    : Color.white);
            }
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

                    row.Index = i;
                    Draw(row, used, _suggestions[i].Icon, _suggestions[i].Text,
                         string.Empty, i == _highlight || i == _hovered,
                         _suggestions[i].Dim);
                }
                return used;
            }

            // No heading row: the box above already shows the name that was picked.
            for (int i = 0; i < _results.Count && used < MaxRows; i++, used++)
            {
                var row = RowAt(used);
                if (row == null) break;

                row.Index = -1;
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
                          bool highlighted, bool dim = false)
        {
            row.Root.transform.localPosition = new Vector3(0f, -RowStep * index, 0f);

            // Every row scrolls a name that does not fit, so all of them stay
            // readable without widening the panel. Offset per row, or they all march
            // in step and the list reads as one moving block.
            text = Marquee(text, index);

            Write(row.Text, text);
            Write(row.Amount, amount);

            row.Icon.sprite = icon;
            row.Icon.enabled = icon != null;

            GameMenu.Tint(row.Text, highlighted ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR
                   : dim ? PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR
                   : Color.white);

            // Sized from the text actually drawn, the way QolStepStrip places its
            // boxes: a guessed collider misses the row at some UI scales.
            float width = Mathf.Max(row.Text.dimensions.width, 4f);
            row.Box.size = new Vector3(width, RowStep, 0.4f);
            row.Box.center = new Vector3(width * 0.5f, 0f, 0f);
        }

        /// Written every frame, unforced, so PugText's own check does the work: it
        /// early-outs on an unchanged string and leaves the glyphs alone.
        ///
        /// GameMenu.SetLiteral forces instead, which a fresh clone needs once to get
        /// glyphs at all - Build does that - but which here rebuilt a count several
        /// times a second as the name beside it scrolled, and it flickered against
        /// its own shadow. Tracking the two separately only desynchronised them.
        private static void Write(PugText text, string value)
        {
            if (text == null) return;

            text.localize = false;
            text.Render(value ?? string.Empty, rewindEffectAnims: false, force: false);
        }

        private void Blank(Row row)
        {
            if (row == null) return;

            Write(row.Text, string.Empty);
            Write(row.Amount, string.Empty);

            row.Icon.enabled = false;
            row.Index = -1;
            row.Box.size = Vector3.zero;
        }

        private void Show(bool visible)
        {
            // PugText releases its glyphs to the pool when disabled, so a row coming
            // back needs a forced render to take them again - an unforced one would
            // early-out on a string it still thinks it is showing.
            if (visible)
            {
                foreach (var row in _pool)
                {
                    GameMenu.SetLiteral(row.Text, string.Empty);
                    GameMenu.SetLiteral(row.Amount, string.Empty);
                }
                GameMenu.SetLiteral(_query, string.Empty);
            }

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

            // The count's two shadow copies are left blank for good. Three texts
            // rendering the same number is what the hover window wants; here the
            // count changes while the name beside it scrolls, and keeping them in
            // step is not worth the rebuild it costs. It reads like every other row
            // without them.
            if (element.chestIcon != null) element.chestIcon.enabled = false;

            if (element.container != null)
            {
                element.container.transform.localPosition = Vector3.zero;
                element.container.SetActive(true);
            }

            element.SR.color = Color.white;

            // The donor puts the count left of the icon, where it sits on top of it
            // and runs off the panel. Moved to the right end of the row and aligned
            // right, so it ends a fixed gap short of the backing however many digits
            // it has. Shadows carried by the same delta, to stay under it.
            // One line: the donor stacks its parts at three different heights, for a
            // hover window that is laid out differently.
            float line = element.text != null
                ? element.text.transform.localPosition.y
                : 0f;

            if (element.SR != null)
            {
                Vector3 icon = element.SR.transform.localPosition;
                element.SR.transform.localPosition = new Vector3(icon.x, line, icon.z);
            }

            if (element.amountNumber != null)
            {
                Vector3 was = element.amountNumber.transform.localPosition;
                var moved = new Vector3(PanelWidth - CountGap, line, was.z);

                element.amountNumber.transform.localPosition = moved;
                element.amountNumber.style.horizontalAlignment =
                    PugTextStyle.HorizontalAlignment.right;
            }

            // A collider with no UIelement on it: UIMouse takes GetComponent<UIelement>
            // off whatever it hits and ignores a null, so this stays out of the
            // game's selection entirely while still giving bounds to hit test.
            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;

            clone.transform.SetParent(transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.SetActive(true);

            // Blanked only now it is active, and all of them, not just the ones this
            // row leaves unused. PugText drops a render made while its object is
            // disabled, so silencing in staging is thrown away and the donor's own
            // ingredient text comes back the moment the row is switched on. Draw
            // refills the two that carry anything.
            foreach (var text in clone.GetComponentsInChildren<PugText>(true))
            {
                Sort(text);
                GameMenu.SetLiteral(text, string.Empty);
            }
            foreach (var sprite in clone.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Sort(sprite);
            }

            return new Row
            {
                Root = clone,
                Icon = element.SR,
                Text = element.text,
                Amount = element.amountNumber,
                Box = box,
                Index = -1,
            };
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

            if (_picked != null)
            {
                _picked.sprite = _suggestions[index].Icon;
                _picked.enabled = _picked.sprite != null;
            }

            _suggestions.Clear();
            Picked?.Invoke(index);

            // Done typing: the results are what matters now, and holding the keyboard
            // would keep the player's own keys suppressed for no reason.
            Blur();
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

        public void MoveCharMarker(int n)
        {
            _caret = Mathf.Clamp(_caret + n, 0, _typed.Length);
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

        /// Escape and Enter both land here. Neither empties the box - the clear
        /// button is the only thing that does, so a search survives letting go of the
        /// keyboard.
        public void Deactivate(bool commit)
        {
            if (commit && _suggestions.Count > 0) Commit();
            Blur();
        }

        public string GetHintString() => "search";

        public bool IsHidden() => false;

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
        }

        /// A window that walks to the end of the name and back, pausing at each. In
        /// whole characters, because the glyphs are on a pixel grid and a smooth
        /// slide would land them between columns.
        private static string Marquee(string text, int row, int width = RowCharacters)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text;

            int over = text.Length - width;
            float span = over / MarqueeRate;
            float pause = 1.2f;
            float cycle = (span + pause) * 2f;

            float at = Mathf.Repeat(Time.unscaledTime + row * 0.4f, cycle);
            float travel = at < span + pause
                ? Mathf.Min(at, span)
                : Mathf.Max(0f, span - (at - span - pause));

            return text.Substring(Mathf.RoundToInt(travel * MarqueeRate), width);
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

                float width = CkQolSearchPanel.PanelWidth;
                float middle = width * 0.5f;
                float boxRow = CkQolSearchPanel.BoxRow;

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

                // The donor's own frame is as wide as a chest window and ran most of
                // the way across the screen. The panel draws its own backing.
                foreach (var sprite in box.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    sprite.enabled = false;
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
                }

                var collider = box.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(width - 1f, 1f, 0.4f);
                collider.center = new Vector3((width - 1f) * 0.5f, 0f, 0f);

                // Room on the left for the picked item's icon.
                box.transform.SetParent(root.transform, false);
                box.transform.localPosition = new Vector3(1.2f, boxRow, 0f);
                box.SetActive(true);

                // After activation, for the same reason the rows are: a render made
                // in staging is dropped, and the donor's "Label..." would come back.
                if (hint != null) GameMenu.SetLiteral(hint, "search...");
                GameMenu.SetLiteral(query, string.Empty);

                // The box's text sits at its own offset inside the donor, so the icon
                // and the clear button take their height from where it actually
                // landed rather than from the row they were placed on.
                float line = root.transform.InverseTransformPoint(query.transform.position).y;

                var picked = Icon(root.transform, donor, new Vector3(0.75f, line, 0f));

                var clear = Clear(root.transform, query,
                                  new Vector3(width - 0.5f, line, 0f),
                                  out BoxCollider clearHit);

                // Ordered by sortingOrder rather than by sibling index, so these can
                // be built last and still sit behind.
                var listPanel = Backing(root.transform, ui,
                                        new Vector3(middle, 0f, 0.2f), width, 1f, query);
                Backing(root.transform, ui,
                        new Vector3(middle, boxRow, 0.2f), width, 1.2f, query);

                panel.Bind(donor, anchor, query, hint, clear, listPanel,
                           ui.playerInventoryUI.backgroundSR, picked, collider, clearHit);

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
                                              Vector3 at, float width, float height,
                                              PugText over)
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

            // One below our own text, on its layer. The donor's order is the
            // inventory window's, which left the item tooltip drawing between this
            // and the text above it - the panel half covered and half not.
            if (over != null)
            {
                sr.sortingLayerID = over.style.sortingLayer;
                sr.sortingOrder = over.style.orderInLayer - 1;
            }

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = at;
            clone.SetActive(true);
            return sr;
        }

        /// A lone icon, cloned off a row so it carries the right material and sorting
        /// order, for whatever the player has picked.
        private static SpriteRenderer Icon(Transform parent,
                                           HoverRequiredMaterialUIElement donor, Vector3 at)
        {
            if (donor == null || donor.SR == null) return null;

            var clone = UnityEngine.Object.Instantiate(donor.SR.gameObject, GameMenu.Staging);
            clone.name = "CkQolSearchIcon";

            var sr = clone.GetComponent<SpriteRenderer>();
            sr.color = Color.white;
            sr.enabled = false;

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = at;
            clone.SetActive(true);
            return sr;
        }

        /// The clear button: the query's own text object cloned, so it matches, with
        /// a collider over it for the panel to hit test.
        private static PugText Clear(Transform parent, PugText style, Vector3 at,
                                     out BoxCollider hit)
        {
            hit = null;
            if (style == null) return null;

            var clone = UnityEngine.Object.Instantiate(style.gameObject, GameMenu.Staging);
            clone.name = "CkQolSearchClear";

            var pug = clone.GetComponent<PugText>();
            pug.maxWidth = 0f;

            // Just the letter. The text is left aligned, so the box starts at its
            // transform and is only as wide as the glyph.
            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.55f, 1f, 0.4f);
            box.center = new Vector3(0.25f, 0f, 0f);
            hit = box;

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = at;
            clone.SetActive(true);

            // Set once active: a render made in staging is dropped.
            GameMenu.SetLiteral(pug, "X");
            return pug;
        }
    }
}
