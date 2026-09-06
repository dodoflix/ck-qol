# Core Keeper QoL

Client-side quality-of-life tweaks, bundled as one PugMod mod.

Settings live in the game's own menu: **Settings → QoL settings**.

Targets Core Keeper **1.2.1.5** (Unity 6000.0.59f2).

## Features

**Auto Fishing** — casts and hooks for you, and stops baited spots depleting.
Casting time sets how far the line lands; Learn casting copies your own charge
instead. Infinite fish shoal is host-side and does nothing on a server without
the mod.

**Auto Eat** — eats when hunger passes a threshold, without changing the item you
have selected. Picks the smallest food that helps, so a cooked dish is not spent
on a small gap. Scopes are independent: hotbar, main inventory, pouches.

**Auto Summon** — keeps minions up as they expire or die. Three modes: the set you
summoned by hand, the type you summoned last, or your cap split between the
summoning weapons on your hotbar. Never summons past the cap, so a change of plan
fills in as minions expire rather than culling healthy ones. Ctrl+R with a
summoning weapon in hand toggles it for the session.

## Install

```sh
./check.sh && ./install.sh
```

Close the game first: mods are compiled at startup.

Deploys to `CoreKeeper_Data/StreamingAssets/Mods/CkQol/`. Set `CK_GAME_DIR` if the
install lives elsewhere. Steam wipes `StreamingAssets/` on updates and on Verify
Integrity — re-run `install.sh` if the mod stops loading.

Config is one JSON per setting under
`…/Pugstorm/Core Keeper/Steam/<id>/mods/CkQol/`. Deleting that directory resets
everything to defaults.

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

## License

MIT — see [LICENSE](LICENSE).
