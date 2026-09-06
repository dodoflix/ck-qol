#!/bin/bash
# Deploy into the client's mod directory. The manifest's file list is generated
# from src/: PugMod silently ignores any .cs that is not listed.
set -euo pipefail

MOD_NAME=CkQol
MOD_GUID=com.dodo.ckqol
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GAME="${CK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Core Keeper}"
DEST="$GAME/CoreKeeper_Data/StreamingAssets/Mods/$MOD_NAME"

[ -d "$GAME" ] || { echo "Core Keeper not found at: $GAME" >&2
                    echo "Set CK_GAME_DIR to override." >&2; exit 1; }

if pgrep -f 'CoreKeeper\.exe' >/dev/null; then
  echo "Core Keeper is running - close it first (mods are compiled at startup)." >&2
  exit 1
fi

rm -rf "$DEST"
mkdir -p "$DEST"
cp -r "$REPO/src" "$DEST/src"

# requiredOn 2 = Server in PugMod's ModExistsOn flags; 1 = Client.
python3 - "$DEST" "$MOD_NAME" "$MOD_GUID" <<'PY'
import json, os, sys
dest, name, guid = sys.argv[1], sys.argv[2], sys.argv[3]
files = []
for root, _, names in os.walk(os.path.join(dest, "src")):
    for n in sorted(names):
        if n.endswith(".cs"):
            rel = os.path.relpath(os.path.join(root, n), dest)
            files.append({"path": rel.replace(os.sep, "/"), "guid": ""})
manifest = {
    "guid": guid,
    "name": name,
    "displayName": "Core Keeper QoL",
    "skipSafetyChecks": False,
    "disableScripts": False,
    "accessesExtraAssemblies": True,
    "disableHarmonyPatching": False,
    "requiredOn": 1,
    "files": files,
    "dependencies": [],
}
with open(os.path.join(dest, "ModManifest.json"), "w") as fh:
    json.dump(manifest, fh, indent=2)
print(f"{len(files)} source file(s):")
for f in files:
    print("  " + f["path"])
PY

echo
echo "installed to: $DEST"
echo "launch the game, then check the log for [CkQol] lines:"
echo "  tail -f \"\$HOME/.local/share/Steam/steamapps/compatdata/1621690/pfx/drive_c/users/steamuser/AppData/LocalLow/Pugstorm/Core Keeper/Player.log\" | grep CkQol"
