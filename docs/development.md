# Development and releasing

## Building

`dotnet build` compiles against the game's assemblies and lints for the APIs PugMod's
security verifier rejects. Run it before every launch: a mod that fails either only says
"Compilation failed" in `Player.log`, hours of guessing later.

`dotnet build -t:Install` also deploys into the game. `CK_GAME_DIR` overrides the default
Steam library path.

To add a feature, write a class in `src/Features/` extending `QolFeatureBase` and register
it in `CkQolMod.BuildFeatures`. It gets a settings page for free.

## Game dump

`dotnet run --project tools/dump` unpacks the game into a gitignored `dump/`: decompiled
assemblies, the GameObject hierarchies as text trees, and every sprite as a PNG. Looking up
how a system is declared, what sits on an object before cloning it, or which icon a name
refers to is then a grep or an image viewer rather than another ILSpy run.

Run it once, and again after a game update; each stage skips what is already current. Add
`-- code <name>` or `-- assets` to run one stage on its own. The shipped build has no
MonoBehaviour typetrees, so the trees carry component class names but not their serialized
values.

## Releasing

Tagging is the trigger:

```sh
dotnet build                      # CI has no game assemblies, so it cannot compile
git tag -a v1.0.0 -m "what changed"
git push origin v1.0.0
```

The Release workflow builds the zip, attaches it to a GitHub release, syncs the mod.io page
(copy, logo, screenshots and tags), uploads the zip to mod.io, and updates the Steam Workshop
item. The tag message becomes the changelog.

Secrets: `MODIO_TOKEN` (a **write**-scoped token from <https://mod.io/me/access>),
`MODIO_MOD_ID`, `STEAM_USERNAME` and `STEAM_CONFIG_VDF`. A step whose secrets are missing is
skipped. The Workshop workflow re-runs only the Workshop update for an existing tag.

`assets/make-logo.py` draws the store logo; `assets/screenshots/` is the store gallery, one
file per image. See the Publishing section of [NOTES.md](../NOTES.md) for the store and
Workshop details.
