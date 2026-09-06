#!/bin/bash
# Create or update the mod.io page from assets/. The release workflow uploads
# builds; this owns the page itself, so the copy lives in git rather than only
# in a web form.
#
#   MODIO_TOKEN=<write-scoped PAT> ./modio-page.sh            # create, hidden
#   MODIO_TOKEN=... MODIO_MOD_ID=123 ./modio-page.sh          # update the copy
#   MODIO_TOKEN=... MODIO_MOD_ID=123 MODIO_VISIBLE=1 ./modio-page.sh
#
# The token needs the write scope. A read-only one fails with error_ref 11139,
# and mint it at https://mod.io/me/access - the api.mod.io host is retired and
# answers 11001, so everything here uses the game domain.
set -euo pipefail

GAME_ID=5289
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API="https://g-$GAME_ID.modapi.io/v1/games/$GAME_ID/mods"

: "${MODIO_TOKEN:?set MODIO_TOKEN to a write-scoped mod.io access token}"

# Unset means "leave it alone" on an update: defaulting it would hide a page
# that is already published every time the copy is edited.
VISIBLE="${MODIO_VISIBLE:-}"

SUMMARY="Client-side quality of life: auto fishing, auto eat, auto summon and a DPS tracker. All optional and configurable from the game's own settings menu. Client-side only, so other players do not need it."

if [ -z "${MODIO_MOD_ID:-}" ]; then
  echo "creating the page (visible=${VISIBLE:-0})"
  curl -sS -X POST "$API" \
    -H "Authorization: Bearer $MODIO_TOKEN" -H 'Accept: application/json' \
    -F "visible=${VISIBLE:-0}" \
    -F 'name=Quality of Life' \
    -F 'name_id=quality-of-life' \
    -F "summary=$SUMMARY" \
    -F "description=<$REPO/assets/modio-description.html" \
    -F 'homepage_url=https://github.com/dodoflix/ck-qol' \
    -F "logo=@$REPO/assets/logo.png" \
    -o response.json
else
  # Edit takes urlencoded fields, not multipart; the logo has its own endpoint.
  echo "updating mod $MODIO_MOD_ID (visible=${VISIBLE:-unchanged})"
  curl -sS -X PUT "$API/$MODIO_MOD_ID" \
    -H "Authorization: Bearer $MODIO_TOKEN" -H 'Accept: application/json' \
    ${VISIBLE:+--data-urlencode "visible=$VISIBLE"} \
    --data-urlencode "summary=$SUMMARY" \
    --data-urlencode "description@$REPO/assets/modio-description.html" \
    --data-urlencode 'homepage_url=https://github.com/dodoflix/ck-qol' \
    -o response.json
fi

python3 - <<'PY'
import json, sys
d = json.load(open("response.json"))
if "error" in d:
    print(json.dumps(d["error"], indent=2), file=sys.stderr)
    sys.exit(1)
print(f"id          {d['id']}")
print(f"visible     {d['visible']}  (0 = hidden)")
print(f"url         {d['profile_url']}")
print()
print(f"gh secret set MODIO_MOD_ID --body {d['id']}")
PY

rm -f response.json
