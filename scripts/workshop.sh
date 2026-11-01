#!/bin/bash
# Pushes a release zip to the existing Steam Workshop item with steamcmd. Only the workflows
# run it. steamcmd cannot set tags, so title, description, images and tags stay as the SDK
# uploader created them (NOTES.md).
#
#   STEAM_USERNAME=... STEAM_CONFIG_VDF=<base64> NOTES="change note" ./scripts/workshop.sh dist/CkQol-1.2.3.zip
#
# STEAM_CONFIG_VDF is a logged-in steamcmd config.vdf, base64 encoded. It carries the login
# token, so no password is needed and Steam Guard is not asked for.
set -euo pipefail

ZIP="${1:?usage: workshop.sh <release zip>}"
WORKSHOP_ID=3831291687
APP_ID=1621690

if [ -z "${STEAM_USERNAME:-}" ] || [ -z "${STEAM_CONFIG_VDF:-}" ]; then
  echo "STEAM_USERNAME or STEAM_CONFIG_VDF is not set - skipping the Workshop update." >&2
  exit 0
fi

sudo apt-get install -qq -y lib32gcc-s1
mkdir -p ~/Steam/config
curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar xz -C ~/Steam
printf '%s' "$STEAM_CONFIG_VDF" | base64 -d > ~/Steam/config/config.vdf

CONTENT="$PWD/workshop-content"
rm -rf "$CONTENT"
unzip -q "$ZIP" -d "$CONTENT"

# Quotes, backslashes and newlines are escaped the way VDF strings expect.
python3 - "$CONTENT" "$WORKSHOP_ID" "$APP_ID" <<'PY'
import os, sys
content, item, app = sys.argv[1:4]
notes = os.environ.get("NOTES", "").replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")
open("workshop.vdf", "w").write(
    '"workshopitem"\n{\n'
    f'"appid" "{app}"\n'
    f'"publishedfileid" "{item}"\n'
    f'"contentfolder" "{content}"\n'
    f'"changenote" "{notes}"\n'
    '}\n')
PY

~/Steam/steamcmd.sh +login "$STEAM_USERNAME" +workshop_build_item "$PWD/workshop.vdf" +quit | tee steamcmd.log
if grep -q -i -E 'error|failed' steamcmd.log; then
  echo "steamcmd reported a failure, see the log above." >&2
  exit 1
fi
