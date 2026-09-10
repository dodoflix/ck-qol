#!/bin/bash
# Dump the game into dump/, so reading its internals is a grep instead of another
# ILSpy run. Gitignored - each machine dumps from its own install.
#
#   ./scripts/dump.sh              everything, skipping what is already current
#   ./scripts/dump.sh code [name]  decompiled assemblies only, optionally filtered
#   ./scripts/dump.sh assets       GameObject hierarchies and sprites only
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="${CK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Core Keeper}"
DATA="$GAME/CoreKeeper_Data"
MANAGED="$DATA/Managed"
OUT="$REPO/dump"
STAGE="${1:-all}"
FILTER="${2:-}"

[ -d "$MANAGED" ] || { echo "game assemblies not found at: $MANAGED" >&2
                       echo "Set CK_GAME_DIR to override." >&2; exit 1; }

dump_code() {
  PATH="$PATH:$HOME/.dotnet/tools"
  command -v ilspycmd >/dev/null || {
    echo "installing ilspycmd..."
    dotnet tool install -g ilspycmd >/dev/null
  }

  # The BCL is documented upstream and would add sixty assemblies of noise to every grep.
  local dlls todo dll cs
  mapfile -t dlls < <(ls "$MANAGED"/*.dll \
    | grep -viE '/(mscorlib|netstandard|System|Microsoft|Mono)\.?[^/]*\.dll$' \
    | { [ -n "$FILTER" ] && grep -i "$FILTER" || cat; })

  [ "${#dlls[@]}" -gt 0 ] || { echo "no assemblies matched: $FILTER" >&2; exit 1; }

  # Only what the last dump missed or a game update replaced.
  todo=()
  for dll in "${dlls[@]}"; do
    cs="$OUT/$(basename "$dll" .dll).decompiled.cs"
    [ -s "$cs" ] && [ "$cs" -nt "$dll" ] || todo+=("$dll")
  done

  if [ "${#todo[@]}" -eq 0 ]; then
    echo "code: up to date, ${#dlls[@]} assemblies"
    return
  fi

  echo "code: decompiling ${#todo[@]} of ${#dlls[@]} assemblies..."
  printf '%s\n' "${todo[@]}" \
    | xargs -d '\n' -P "$(nproc 2>/dev/null || echo 4)" -I{} bash -c '
        out="$1/$(basename "$3" .dll).decompiled.cs"
        # A partial file would look current to the next run, so drop it on failure.
        ilspycmd -o "$1" -r "$2" "$3" >/dev/null 2>&1 \
          || { rm -f "$out"; echo "failed: $(basename "$3")" >&2; }
      ' _ "$OUT" "$MANAGED" {}
  echo "  $(ls "$OUT"/*.decompiled.cs | wc -l) assemblies"
}

dump_assets() {
  local marker="$OUT/assets/resources.assets.tree.txt"
  if [ -s "$marker" ] && [ "$marker" -nt "$DATA/resources.assets" ]; then
    echo "assets: up to date, $(ls "$OUT/assets" | wc -l) trees"
    return
  fi
  echo "assets: reading the game's data files..."
  if command -v uv >/dev/null; then
    uv run --quiet --with UnityPy python "$REPO/scripts/dump-assets.py" "$DATA" "$OUT"
  else
    local venv="$OUT/.venv"
    [ -x "$venv/bin/python" ] || { python3 -m venv "$venv"
                                   "$venv/bin/pip" install -q UnityPy; }
    "$venv/bin/python" "$REPO/scripts/dump-assets.py" "$DATA" "$OUT"
  fi
}

mkdir -p "$OUT"
case "$STAGE" in
  all)    dump_code; dump_assets ;;
  code)   dump_code ;;
  assets) dump_assets ;;
  *)      echo "usage: $0 [all|code [name]|assets]" >&2; exit 1 ;;
esac

echo
echo "$(du -sh "$OUT" | cut -f1) in dump/"
