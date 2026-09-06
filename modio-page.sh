#!/bin/bash
# Push the mod.io page from this repo: copy, logo and tags. The release workflow
# runs it on every tag, so the store page is generated from source rather than
# edited in a web form and then forgotten.
#
#   MODIO_TOKEN=<write-scoped PAT> ./modio-page.sh                  # create, hidden
#   MODIO_TOKEN=... MODIO_MOD_ID=6363554 ./modio-page.sh            # update
#   MODIO_TOKEN=... MODIO_MOD_ID=6363554 MODIO_VISIBLE=1 ./modio-page.sh
#
# The token needs the write scope. A read-only one fails with error_ref 11139;
# mint one at https://mod.io/me/access. The api.mod.io host is retired and
# answers 11001, so everything here uses the game domain.
set -euo pipefail

GAME_ID=5289
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API="https://g-$GAME_ID.modapi.io/v1/games/$GAME_ID/mods"

: "${MODIO_TOKEN:?set MODIO_TOKEN to a write-scoped mod.io access token}"

# Unset means "leave it alone" on an update: defaulting it would hide a page
# that is already published every time the copy is edited.
VISIBLE="${MODIO_VISIBLE:-}"

SUMMARY="Client-side quality of life: auto fishing, auto eat, auto summon, a DPS tracker and chest search. All optional and configurable from the game's own settings menu. Client-side only, so other players do not need it."

# Must be tags the game defines - GET /games/5289 lists them under tag_options.
# Bump the version tag when the mod is verified against a newer build.
TAGS=("1.2.1.5" "Quality of Life" "Client" "Script")

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

auth=(-H "Authorization: Bearer $MODIO_TOKEN" -H 'Accept: application/json')

# Fails the script on an API error rather than letting a later step run against
# a page that was never updated.
check() {
  python3 - "$1" "$2" <<'PY'
import json, sys
step, path = sys.argv[1], sys.argv[2]
d = json.load(open(path))
if "error" in d:
    print(f"{step} failed:", json.dumps(d["error"], indent=2), file=sys.stderr)
    sys.exit(1)
PY
}

if [ -z "${MODIO_MOD_ID:-}" ]; then
  echo "creating the page (visible=${VISIBLE:-0})"
  curl -sS -X POST "$API" "${auth[@]}" \
    -F "visible=${VISIBLE:-0}" \
    -F 'name=Quality of Life' \
    -F 'name_id=quality-of-life' \
    -F "summary=$SUMMARY" \
    -F "description=<$REPO/assets/modio-description.html" \
    -F 'homepage_url=https://github.com/dodoflix/ck-qol' \
    -F "logo=@$REPO/assets/logo.png" \
    -o "$WORK/mod.json"
  check "create" "$WORK/mod.json"
  MODIO_MOD_ID=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["id"])' "$WORK/mod.json")
else
  # Edit takes urlencoded fields, not multipart; media has its own endpoint.
  echo "updating the copy on mod $MODIO_MOD_ID (visible=${VISIBLE:-unchanged})"
  curl -sS -X PUT "$API/$MODIO_MOD_ID" "${auth[@]}" \
    ${VISIBLE:+--data-urlencode "visible=$VISIBLE"} \
    --data-urlencode "summary=$SUMMARY" \
    --data-urlencode "description@$REPO/assets/modio-description.html" \
    --data-urlencode 'homepage_url=https://github.com/dodoflix/ck-qol' \
    -o "$WORK/mod.json"
  check "update" "$WORK/mod.json"

  # Retried: this one answered curl 56 mid-release once, which under set -e took
  # the file upload down with it and left the page on the previous version.
  echo "uploading the logo"
  curl -sS --retry 3 --retry-all-errors --retry-delay 2 \
    -X POST "$API/$MODIO_MOD_ID/media" "${auth[@]}" \
    -F "logo=@$REPO/assets/logo.png" \
    -o "$WORK/media.json"
  check "logo" "$WORK/media.json"
fi

# Tags are added and removed, never replaced, so stale ones have to go
# explicitly - otherwise last release's game version stays on the page forever.
echo "syncing tags: ${TAGS[*]}"
curl -sS "$API/$MODIO_MOD_ID/tags" "${auth[@]}" -o "$WORK/tags.json"
check "read tags" "$WORK/tags.json"

mapfile -t STALE < <(python3 - "$WORK/tags.json" "${TAGS[@]}" <<'PY'
import json, sys
keep = set(sys.argv[2:])
for tag in json.load(open(sys.argv[1])).get("data", []):
    if tag["name"] not in keep:
        print(tag["name"])
PY
)

if [ "${#STALE[@]}" -gt 0 ]; then
  echo "  removing: ${STALE[*]}"
  args=()
  for tag in "${STALE[@]}"; do args+=(--data-urlencode "tags[]=$tag"); done
  curl -sS -X DELETE "$API/$MODIO_MOD_ID/tags" "${auth[@]}" "${args[@]}" -o "$WORK/untag.json"
fi

# Urlencoded, not multipart: the tag endpoints answer 13006 to a -F body.
args=()
for tag in "${TAGS[@]}"; do args+=(--data-urlencode "tags[]=$tag"); done
curl -sS -X POST "$API/$MODIO_MOD_ID/tags" "${auth[@]}" "${args[@]}" -o "$WORK/tag.json"
check "add tags" "$WORK/tag.json"

curl -sS "$API/$MODIO_MOD_ID" "${auth[@]}" -o "$WORK/final.json"
python3 - "$WORK/final.json" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
print(f"id          {d['id']}")
print(f"visible     {d['visible']}  (0 = hidden)")
print(f"tags        {', '.join(t['name'] for t in d.get('tags', []))}")
print(f"url         {d['profile_url']}")
PY
