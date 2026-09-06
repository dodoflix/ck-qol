using System;
using System.Collections.Generic;
using UnityEngine;

namespace CkQol.Native
{
    /// One line of results: a container icon, where it is, and how many.
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
        public Func<string, List<string>> Suggest;
        public Action<int> Picked;
        public Action Cleared;

        /// 15px, the step the HUD stacks its own widgets by.
        private const float RowStep = 0.9375f;
        private const int MaxRows = 10;
        private const int MaxLength = 32;

        private class Row
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public PugText Text;
            public PugText Amount;
            public PugText[] AmountShadows;
            public CkQolSearchPick Pick;
            public string Shown;
        }

        private HoverRequiredMaterialUIElement _donor;
        private Transform _anchor;

        private PugText _query;

        private readonly List<Row> _pool = new List<Row>();
        private readonly List<string> _suggestions = new List<string>();
        private readonly List<SearchRow> _results = new List<SearchRow>();

        private string _typed = string.Empty;
        private int _caret;
        private int _highlight;
        private bool _focused;
        private bool _active;
        private string _title;
        private string _shownQuery;

        internal void Bind(HoverRequiredMaterialUIElement donor, Transform anchor, PugText query)
        {
            _donor = donor;
            _anchor = anchor;
            _query = query;
        }

        /// Handed the current results by the feature.
        public void SetRows(string title, List<SearchRow> rows)
        {
            _title = title;
            _results.Clear();
            if (rows != null) _results.AddRange(rows);
        }

        /// Takes the keyboard. Deliberately not automatic on opening the inventory:
        /// DisableInput stops every player key, the close key included, so the player
        /// has to ask for the box before it can trap them in it.
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

            string shownQuery = _typed + (_focused ? "_" : string.Empty);
            if (shownQuery != _shownQuery)
            {
                GameMenu.SetLiteral(_query, shownQuery);
                _shownQuery = shownQuery;
            }

            int used = 0;
            if (_suggestions.Count > 0)
            {
                for (int i = 0; i < _suggestions.Count && used < MaxRows; i++, used++)
                {
                    var row = RowAt(used);
                    if (row == null) break;

                    row.Pick.Index = i;
                    Draw(row, used, null,
                         (i == _highlight ? "> " : "  ") + _suggestions[i],
                         string.Empty);
                }
            }
            else
            {
                if (_title != null && used < MaxRows)
                {
                    var head = RowAt(used);
                    if (head != null)
                    {
                        head.Pick.Index = -1;
                        Draw(head, used, null, _title, string.Empty);
                        used++;
                    }
                }

                for (int i = 0; i < _results.Count && used < MaxRows; i++, used++)
                {
                    var row = RowAt(used);
                    if (row == null) break;

                    row.Pick.Index = -1;
                    Draw(row, used, _results[i].Icon, _results[i].Text, _results[i].Amount);
                }
            }

            for (int i = used; i < _pool.Count; i++) Blank(_pool[i]);
        }

        /// Beside the inventory, following it rather than a screen corner - the HUD
        /// has no anchoring of its own, every widget carries a position.
        private void Place()
        {
            if (_anchor == null) return;

            Vector3 at = _anchor.position;
            transform.position = new Vector3(at.x + 7f, at.y, at.z);
        }

        private void Draw(Row row, int index, Sprite icon, string text, string amount)
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
        }

        private void Blank(Row row)
        {
            if (row == null || row.Shown == string.Empty) return;

            GameMenu.SetLiteral(row.Text, string.Empty);
            GameMenu.SetLiteral(row.Amount, string.Empty);
            foreach (var shadow in row.AmountShadows) GameMenu.SetLiteral(shadow, string.Empty);

            row.Icon.enabled = false;
            row.Pick.Index = -1;
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

            // The row draws the name on the left and the count on the right, which is
            // what the hover window uses these two for as well.
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

            var pick = clone.AddComponent<CkQolSearchPick>();
            pick.Panel = this;
            pick.Index = -1;
            pick.gameObject.layer = _anchor != null ? _anchor.gameObject.layer : clone.layer;

            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(6f, RowStep, 0.1f);
            box.center = new Vector3(2.5f, 0f, 0f);

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
            else if (!commit)
            {
                _typed = string.Empty;
                _caret = 0;
                _suggestions.Clear();
                Cleared?.Invoke();
            }

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

    /// A clickable row. Same mechanism as QolStepStrip: a collider plus a
    /// ButtonUIElement subclass, because a prefab's persistent call cannot be
    /// re-pointed at runtime.
    public class CkQolSearchPick : ButtonUIElement
    {
        public CkQolSearchPanel Panel;
        public int Index = -1;

        protected override void Awake()
        {
            // Awake walks both lists; a runtime-added component has them null.
            if (spritesShownUnpressed == null) spritesShownUnpressed = new List<SpriteRenderer>();
            if (spritesShownPressed == null) spritesShownPressed = new List<SpriteRenderer>();
            base.Awake();
        }

        public override void OnLeftClicked(bool mod1, bool mod2)
        {
            base.OnLeftClicked(mod1, mod2);
            if (Panel == null) return;

            if (Index >= 0) Panel.Choose(Index);
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

                var root = new GameObject("CkQolSearchPanel");
                root.layer = anchor.gameObject.layer;
                root.transform.SetParent(anchor.parent, false);

                // The donor's own script implements this same interface and would
                // fight for the active field; only its text and backing sprites are
                // wanted.
                var box = UnityEngine.Object.Instantiate(field.gameObject, GameMenu.Staging);
                box.name = "CkQolSearchBox";
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

                var query = box.GetComponentInChildren<PugText>(true);

                var panel = root.AddComponent<CkQolSearchPanel>();

                var focus = box.AddComponent<CkQolSearchPick>();
                focus.Panel = panel;
                focus.Index = -1;
                var collider = box.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(6f, 1f, 0.1f);
                collider.center = new Vector3(2.5f, 0f, 0f);

                box.transform.SetParent(root.transform, false);
                box.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                box.SetActive(true);

                panel.Bind(donor, anchor, query);

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
    }
}
