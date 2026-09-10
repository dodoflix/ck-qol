#!/bin/bash
# Deploy into the client's mod directory.
set -euo pipefail

MOD_NAME=CkQol
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

GAME="${CK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Core Keeper}"
DEST="$GAME/CoreKeeper_Data/StreamingAssets/Mods/$MOD_NAME"

[ -d "$GAME" ] || { echo "Core Keeper not found at: $GAME" >&2
                    echo "Set CK_GAME_DIR to override." >&2; exit 1; }

if pgrep -f 'CoreKeeper\.exe' >/dev/null; then
  echo "Core Keeper is running - close it first (mods are compiled at startup)." >&2
  exit 1
fi

"$REPO/scripts/package.sh" "$DEST"

echo
echo "installed to: $DEST"
echo "launch the game, then check the log for [CkQol] lines:"
echo "  tail -f \"\$HOME/.local/share/Steam/steamapps/compatdata/1621690/pfx/drive_c/users/steamuser/AppData/LocalLow/Pugstorm/Core Keeper/Player.log\" | grep CkQol"
