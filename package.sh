#!/bin/bash
# Lay the mod out the way the game loads it: ModManifest.json beside src/.
# Used by install.sh for a local deploy and by the release workflow for the
# mod.io upload, so the two cannot drift.
#
# usage: ./package.sh <destination-dir>
set -euo pipefail

MOD_NAME=CkQol
MOD_GUID=com.dodo.ckqol
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DEST="${1:-}"
[ -n "$DEST" ] || { echo "usage: $0 <destination-dir>" >&2; exit 1; }

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
