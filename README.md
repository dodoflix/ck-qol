# Core Keeper QoL

Client-side quality-of-life tweaks, bundled as one PugMod mod.

Settings live in the game's own menu: **Settings → QoL settings**.

Targets Core Keeper **1.2.1.5** (Unity 6000.0.59f2).

## Features

**Auto Fishing** — casts and hooks for you, and stops baited spots depleting.
Configurable cast charge, hook press and shoal behaviour. Infinite shoal is
host-side and does nothing on a server without the mod.

## Install

```sh
./check.sh && ./install.sh
```

Close the game first: mods are compiled at startup.

Deploys to `CoreKeeper_Data/StreamingAssets/Mods/CkQol/`. Set `CK_GAME_DIR` if
the install lives elsewhere. Steam wipes `StreamingAssets/` on updates and on
Verify Integrity — re-run `install.sh` if the mod stops loading.

## How it works

PugMod ships **C# source**, not a DLL: the game carries Roslyn and compiles mods
in-process at startup. So there is no build step, and mistakes only surface in
`Player.log` after a full launch.

`check.sh` closes that gap. It compiles against `CoreKeeper_Data/Managed/`, then
lints for APIs PugMod's **security verifier** rejects — `System.Reflection` above
all. A clean compile says nothing about passing the verifier, and the only
in-game symptom is a bare "Compilation failed".

`install.sh` generates `ModManifest.json` from `src/`. PugMod silently ignores
any `.cs` missing from the manifest.

## The settings menu

No custom UI: the mod **clones the game's real option rows** and swaps in its own
behaviour, so navigation, fonts, sounds and controller support come from the
game.

Gotchas in `Native/`, all found the hard way:

- Menus are **SpriteRenderer world-space UI** driven by `RadicalMenu`, not uGUI.
- Rows live under a **container inside the menu**, which carries an offset. A row
  parented to the menu root gets the right local position and the wrong world
  position. Clone with `parent: null` to land beside the donor.
- `menuOptions` is filled in `Awake` **only**, and option menus are instantiated
  inactive, so it is empty at startup. Read rows with `GetComponentsInChildren`.
- `UpdatePosition` always lays out, but menus that never auto-position ship
  `menuEntryVirtualHeight` at zero, stacking every row on one line. Give it a
  measured pitch.
- `extraVerticalSpacing` is subtracted before placement, so a clone inherits the
  donor's group gap and lands low.
- `PugText.Render` treats its argument as a **localization key** unless `localize`
  is false, and early-outs unless `force: true`.
- A ranged donor carries prefab-wired per-diamond `ButtonUIElement`s. Swapping the
  script leaves them pointing at a destroyed component, where they win the click
  raycast and do nothing. `QolStepStrip` destroys and rebuilds them.
- Donors are matched by shape (`isOnOffToggle`, type test), never class name.

Two PugMod bugs worked around in `ModSetting`:

- `ModConfigEntry`'s **getter** re-reads and re-parses the JSON on every access,
  so values are cached here rather than read back.
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

    public override void Update() => DoSomething(_strength.Value);
}
```

Register it in `CkQolMod.BuildFeatures`. It gets a page, an Enabled toggle, a
config section named after `Name`, and a row per setting.

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

## Scope

Client-side, `requiredOn: 1`. Other players do not need it. Anything
server-authoritative — world state, entity spawning, world generation — is out of
scope, except where a system can run host-side for a listen server.

## License

MIT — see [LICENSE](LICENSE).
