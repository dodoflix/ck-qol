# Notes

How the mod works, and what it cost to find out. The README is the short version.

## How it works

PugMod ships **C# source**, not a DLL: the game carries Roslyn and compiles mods
in-process at startup. So there is no build step, and mistakes only surface in
`Player.log` after a full launch.

`check.sh` closes that gap. It compiles against `CoreKeeper_Data/Managed/`, then
lints for APIs PugMod's **security verifier** rejects — `System.Reflection` above
all. A clean compile says nothing about passing the verifier, and the only in-game
symptom is a bare "Compilation failed".

`install.sh` generates `ModManifest.json` from `src/`. PugMod silently ignores any
`.cs` missing from the manifest.

## Using an item without changing the player's selection

All three features do this, and it is the load-bearing trick of the mod.

Eating, summoning and reeling all consume the **equipped** slot — there is no path
that uses an item from an arbitrary inventory index. But `ClientInput` is writable,
and the ordering works out:

```
SimulationSystemGroup
├─ RunSimulationSystemGroup                       (OrderFirst)
│    ├─ SendClientInputSystem                     (OrderLast)
│    └─ our systems                               (UpdateAfter) ← write here
└─ …BeforePredictedFixedStepSimulationSystemGroup
     └─ EquipmentSystemGroup
          ├─ EquipmentBeforeUpdateSystemGroup → SelectedEquipmentChangeSystem  ← reads it
          └─ EquipmentUpdateSystemGroup       → EquipmentUpdateSystem → *Slot
```

Writing `ClientInput.equippedSlotIndex` reaches `SelectedEquipmentChangeSystem` on
the same tick. The player's real selection lives in a client MonoBehaviour that
`SendClientInputSystem` copies back wholesale every tick, so the override lasts
exactly as long as it is written and the hotbar never moves.

`PlayerSlots` holds the shared parts. Things learned the hard way:

- **End the press with the slot still overridden.** `EquipmentUpdateSystem` latches a
  pending second-interact, so dropping the button and the override together lets
  that latch land a frame later on whatever the player actually holds — firing
  their weapon.
- **Pin the aim** while pressing, or summons go to the aim marker, up to twelve
  tiles away for command-minion weapons.
- `equippedSlotIndex` is a **byte**, and `SelectedEquipmentChangeSystem` indexes the
  buffer with it and no bounds check.
- The inventory is one `ContainedObjectsBuffer`; `InventoryBuffer[0]` is the main
  inventory and `1..` are pouches. The **hotbar is not a container** but a moving
  window over those, so scope has to be decided per slot.
- An open inventory does **not** need to block any of this — the simulation never
  checks the UI. A held cursor item does.

## The settings menu

No custom UI: the mod **clones the game's real option rows** and swaps in its own
behaviour, so navigation, fonts, sounds and controller support come from the game.

- Menus are **SpriteRenderer world-space UI** driven by `RadicalMenu`, not uGUI.
- Rows live under a **container inside the menu**, which carries an offset. Clone
  with `parent: null` to land beside the donor.
- `menuOptions` is filled in `Awake` **only**, and option menus are instantiated
  inactive, so it is empty at startup. Read rows with `GetComponentsInChildren`.
- `UpdatePosition` always lays out, but menus that never auto-position ship
  `menuEntryVirtualHeight` at zero, stacking every row on one line.
- `extraVerticalSpacing` is subtracted before placement, so a clone inherits the
  donor's group gap and lands low.
- A ranged donor carries prefab-wired per-diamond `ButtonUIElement`s. Swapping the
  script leaves them pointing at a destroyed component, where they win the click
  raycast and do nothing. `QolStepStrip` destroys and rebuilds them.

## The HUD key hint

`GameHints` adds a line to the bottom-right hints by cloning a stock one.
`InGameButtonHintsUI.hintButtonRows` is a plain public list with no cached index —
appending is safe, inserting at 0 is not, because the row anchor is read live from
`buttons[0]`.

Every one of these produced an invisible hint:

- The label is a **child of `textContainer`**, which the stock hints toggle. An
  active label inside an inactive parent renders nothing.
- `PugText.Start()` **deactivates its own GameObject** when `renderOnStart` is
  false, and never sets `startCalled`, so `OnEnable` never re-renders it. Only a
  successful `Render()` revives it.
- `PugText` releases its glyphs to the pool when disabled, so text rendered into a
  disabled object is dropped. Activate first, render second.
- A cloned `PugText` inherits the donor's `maxWidth`, sized for labels like "Tab",
  and wraps one word per line.
- `CalcGameplayUITargetScaleMultiplier()` returns **zero** for roughly the first
  second in a world, and during any fade. Sampling it once and caching leaves the
  hint at zero size forever.
- Never deactivate a sprite's GameObject to hide it — the donor's sprites can be
  the label's own parent. Disable the renderer.
- **Set `renderOnStart`, `keepEnabledOnStart` and `freeResourcesOnDisable` on every
  cloned `PugText`** — `GameMenu.KeepRendered`. The prefabs ship all three off, and
  together they make a toggled clone keep the donor's words for good: hiding it
  leaves the glyphs on screen instead of returning them to the pool (`:305`),
  `OnEnable` never redraws because `startCalled` was never set (`:287`, `:273`), and
  a blank written meanwhile still records `textString` — so `HasCorrectGlyphs`
  matches against glyphs nobody drew (`:666`) and every later write early-outs. This
  is the stuck ingredient row, and it took three attempts to find because blanking
  after activation fixes the first open and nothing after it.
- Blank a clone **after** activating it either way: a render made while the object is
  disabled is dropped. `SetLiteral` forces, which a fresh clone needs once;
  everything after should render unforced and let `PugText` early-out, or a count
  rebuilds its glyphs every time the name beside it changes and flickers.

## The stat panel

`GameStatPanel` stacks icon-and-number rows under the minion counter, cloned from the
hover window's ingredient row — `HoverRequiredMaterialUIElement` is five public fields
and no logic, and is already an icon beside a number.

The HUD has no anchoring helper and no screen-corner maths anywhere: every widget
carries a hardcoded position and rewrites its own y from the one above it
(`MinionCountUI.cs:51-54`, `PlayerHungerBarUI.cs:83-92`). The panel does the same, and
has to cope with the minion counter hiding itself entirely at zero minions.

## Reading the damage you deal

The client is told the exact damage of every hit it draws a number for, **and who dealt
it**: `EffectEventCD.value1` and `entity2` are both `[GhostField]`, replicated to every
client. Only the damage-number UI throws the attacker away
(`EffectEventExtensions.cs:224`).

`PlayLocalEffectEventSystem` de-duplicates each replicated event against the client's
own predicted copy and records what it played in `LocalEffectEventBuffer`. Reading that
buffer, ordered after that system, gives the de-duplicated stream for free — and it is
ten deep where the replicated ring it comes from is three.

An attacker is the player's if walking `OwnerReferenceCD.owner` reaches them, which is
what the server itself does in `EntityUtility.GetOwnerInfo`. The row is named by the
first thing in that chain the player carries a weapon for — `SecondaryUseCD.minionToSpawn`
for a staff, `RangeWeaponCD.projectileID` for a gun — so an explosion owned by a
projectile owned by the player lands on the gun that fired it. Only the player's own
swing, where they *are* the attacker, uses the item in hand.

Never the spawned entity's own `ObjectDataCD`: that names the prefab the weapon puts on
the field, and the Grubzooka's is a mining projectile that draws as a pickaxe.

Three things it cannot see:

- **Who applied a condition.** Burning and acid ticks carry no attacker at all, so that
  row counts every tick on nearby enemies, other players' included. It has a setting.
- **What threw a bomb.** Nothing links an explosive back to an item, so it resolves to
  no weapon and is left out rather than credited to whatever happens to be in hand.
- **More than three effects on one entity between snapshots.** The replicated ring
  overwrites, and damage numbers share it with unrelated effects, so wide AoE reads low.

## Searching what is in a container

Chest contents are already on the client: `ContainedObjectsBuffer` is
`PrefabType = All`, `SendMask = AllClients`, `SendToOwner = All`. Nothing has to be
opened and no request is sent. The vanilla client does a superset of this every
frame — `CraftingHandler` reads every nearby chest to grey out recipes, and records
which one holds a material.

Containers are found with a plain entity query over `ContainedObjectsBuffer` +
`InventoryBuffer` + `LocalTransform`, excluding `PlayerGhost`. Not the physics
helper the game's own quick stack uses: that matches on
`InventoryAutoTransferEnabledCD`, which marks chests but not every crafting station.
`InventoryBuffer` is what keeps out everything else that carries an object without
being a container.

Two things bound it:

- **Ghost relevancy, about ±22 by ±14 tiles.** Outside that rect the container
  entity does not exist on the client at all, so no radius setting can reach it.
- Counts come from `ContainedObjectsBuffer` only. `ObjectDataCD.amount` on a
  container is the world-label visibility state, not a count.

`amount` in a slot is a stack count only for something stackable; on a weapon or a
piece of armour it is the **durability**, which read as four hundred iron swords in
one chest until those were counted one per slot. An item lying on the floor carries
its contents the same way a container does and the query finds it too — the
game's own pick-up system tells them apart by `PickUpItemCD`, so this does.

Pointing at a container reuses the outline the game rings the interactable you are
standing next to with — `OutlineController.SetColor` plus `SpriteObject.outlineColor`,
which is what `EntityMonoBehaviour.UpdateOutline` writes — and floats the count over
it as a `CombatText`. Two things about that:

- **Re-apply the colour every frame.** The game drives the outline of whatever is
  closest to the player, and putting that one back resets it for every container
  sharing its prefab, taking ours with it.
- The floating count is slower than the rescan, because it drifts and fades:
  re-spawning it twice a second smears it into an unreadable stack. It is the
  game's damage number, so it takes one of five colours rather than the outline's
  own, carries no icon, and does not appear at all for a player who has turned
  damage numbers off (`CombatText.SpawnCombatText` returns on
  `Manager.prefs.showDamageNumbers`). The outline is the part that always shows.

Suggestions start at the first character. They cost one keystroke, not one frame:
a walk of the nearby containers to learn what is in stock, then up to four passes
over the name index — nearby-and-prefix, nearby, prefix, the rest — each stopping
at eight results. So the first letter offers what you already own before anything
else, and a single letter is no more expensive than three.

Item names go through `PugText.ProcessText("Items/" + property, …)` rather than
I2.Loc directly, which is not among the assemblies the mod compiles against. A
missing term comes back as `missing: …` rather than null, and that is the test for
whether an `ObjectID` is a real named item.

## Typing in the world

Registering as `Manager.input.activeInputField` is enough to receive keystrokes
anywhere: `MenuManager.HandleTypingInput` feeds it `Input.inputString` and runs
before any menu check. `QolTextOption` already implements that interface for the
settings menu and the search box reuses it.

Four things differ outside a menu:

- Pair it with `Manager.input.DisableInput()` / `EnableInput()`, or the player walks
  while typing. The pause menu has already frozen them, so the settings row skips it.
- `DisableInput` stops **every** player key, including the one that closes the
  inventory, so focus is taken by clicking the box rather than automatically — a
  panel that grabs input on open would trap the player in it. Without it the
  inventory shortcuts still fire while typing, and an `f` in a search locks a slot.
- It also stops the click that selects a UI element, so the panel hit-tests its own
  rows against `Manager.ui.mouse.pointer` rather than relying on `UIMouse`.
- **The panel has to be a `UIelement`.** `UIMouse.TrySelectNewElement` casts
  `Manager.input.activeInputField` to one with no test, so a plain MonoBehaviour in
  that field throws every frame and kills hovering everywhere while the box is
  focused.

Up and down arrows are polled directly. `MenuManager` only forwards left and right
to an input field, as caret movement.

## PugMod quirks worked around

- `PugDatabase.GetBuffer` and `GetComponent` **do not check the prefab has the
  component** before fetching it, unlike `TryGetComponent`. Guard with
  `HasComponent` or they throw on anything that lacks it.
- `ModConfigEntry`'s **getter** re-reads and re-parses the JSON on every access, so
  `ModSetting` caches the value and touches the entry only at bind and on write.
- `ModAPIConfig.Register` writes a new file through `Set()`, which omits
  `description` and `defaultValue`; later runs read that back, so they stay empty
  forever. One extra write on a new file fixes it.

## Adding a feature

Create a class in `src/Features/` extending `QolFeatureBase`:

```csharp
public class MyTweak : QolFeatureBase
{
    public override string Name => "My Tweak";
    public override string Description => "Shown on hover.";

    private readonly FloatSetting _strength =
        new FloatSetting("Strength", "Strength", 1f, 0f, 5f, "Tooltip.");

    public override IEnumerable<ModSetting> GetSettings() { yield return _strength; }

    /// Called on start, on stop, and on every setting change.
    protected override void Apply() => MyState.Strength = Running ? _strength.Value : 0f;
}
```

Register it in `CkQolMod.BuildFeatures`. It gets a page, an Enabled toggle, a config
section named after `Name`, and a row per setting.

| setting | row |
|---|---|
| `BoolSetting` | `on` / `off` |
| `IntSetting`, `FloatSetting` | diamond bar, click a diamond to jump |
| `ChoiceSetting` | cycles, stored by name |
| `StringSetting` | text field, on the game's own input pipeline |
| `KeySetting` | rebind, polled through Rewired |

**`Name` is the config section.** Renaming it orphans saved settings.

A feature that throws in any callback is logged, disabled, and the rest keep
running.

An ECS system reads its settings from a static mirror rather than the feature, so
it holds no reference and costs one bool test while off. `Apply()` gates on
`Running`, or editing a setting would restart a disabled feature.

### If it drives the player's input

Follow the three existing systems rather than inventing a fourth shape:

```csharp
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(SendClientInputSystem))]
public partial class CkQolMyTweakSystem : PugSimulationSystemBase
```

- `OnUpdate` calls a `Tick()` that returns plainly; `base.OnUpdate()` is called once,
  not at every guard.
- Guard cheapest first. The gate that is true almost every frame goes at the top, so
  an idle frame costs one component read.
- Call `UseButton.Claim(this)` before touching `ClientInputData`, and do nothing that
  frame if it refuses.
- Use `PlayerSlots.Press` / `EndPress` rather than writing the input directly — they
  carry the release rule that stops a latched press firing the player's weapon.
- Stop while `Manager.menu.IsAnyMenuActive()` or `PlayerSlots.DragInProgress()`. An
  open inventory is fine.
- Throttle anything that walks the inventory or queries entities; those are
  main-thread sync points, not free reads.

## Sharing the use button

Every automatic feature drives the same `SecondInteract` bit, and two writing it in
one frame is undefined. `UseButton` arbitrates: a feature calls `Claim(this)` before
touching the input and does nothing that frame if it returns false.

A claim is first come, and held only while it keeps being re-asserted. A holder that
stops asking — feature switched off, world gone, or simply finished — loses it on the
next frame, so a claim cannot leak and no cleanup path can forget to release one.
Holds last as long as a press, so a waiting feature waits a fraction of a second.

Separately, Auto Eat and Auto Summon refuse to act while the player is fishing. That
is **not** contention: equipping food or a staff leaves the fishing state, because
the game exits it as soon as the equipped item is not a rod, so the cast would be
cancelled.

## Scope

Client-side, `requiredOn: 1`. Other players do not need it. Anything
server-authoritative is out of scope, except where a system can run host-side for a
listen server.


## Publishing

`modio-page.sh` owns the mod.io page — summary, description, logo and tags — so the
store copy is generated from `assets/` rather than edited in a web form and then left
to drift. Core Keeper is game `5289` and the mod is `6363554`.

- `api.mod.io` is retired and answers error 11001; everything uses `g-5289.modapi.io`.
- A read-only token fails writes with 11139, which reads like a permissions bug on the
  mod rather than on the token.
- Tags must be ones the game defines (`GET /games/5289` lists them under
  `tag_options`), and are added and removed rather than replaced — otherwise last
  release's game-version tag stays on the page forever. That version is a constant in
  the script and needs bumping when the mod is verified against a newer build.
- The tag endpoints answer 13006 to a multipart body: urlencoded only.
- `ModManifest.json` goes at the **root** of the zip. The game unpacks a download
  flat, so a wrapping folder installs a mod it then cannot find.
