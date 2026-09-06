#!/bin/bash
# Compile the mod against the game's own assemblies.
#
# PugMod compiles mod source in-process at game startup, so without this the first
# report of a typo is a line in Player.log after a full launch. Generates a
# throwaway csproj in a temp dir; nothing is written to the repo.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="${CK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Core Keeper}"
MANAGED="$GAME/CoreKeeper_Data/Managed"

[ -d "$MANAGED" ] || { echo "game assemblies not found at: $MANAGED" >&2
                       echo "Set CK_GAME_DIR to override." >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Unity's own mscorlib/netstandard must NOT be referenced - they shadow the SDK's
# core libs and every predefined type fails to resolve.
{
  cat <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$REPO/src/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
EOF
  for dll in UnityEngine.CoreModule UnityEngine.PhysicsModule UnityEngine UnityEngine.InputLegacyModule \
             PugMod.SDK.Runtime PugMod.SDK Pug.Other Pug.Base Rewired_Core \
             Unity.Entities Unity.Mathematics Unity.Collections; do
    [ -f "$MANAGED/$dll.dll" ] &&
      echo "    <Reference Include=\"$dll\"><HintPath>$MANAGED/$dll.dll</HintPath><Private>false</Private></Reference>"
  done
  printf '  </ItemGroup>\n</Project>\n'
} > "$WORK/check.csproj"

dotnet build "$WORK/check.csproj" -v q --nologo 2>&1 \
  | grep -E "error CS|warning CS|Build succeeded" \
  | sed "s|$REPO/||" \
  | sort -u

# PugMod runs a security verifier on the compiled assembly and refuses to load
# anything touching these. A clean compile says nothing about passing it, so the
# first sign is otherwise "a mod failed to load: Compilation failed" in game.
echo
banned=0
for pattern in 'System\.Reflection' '\.GetType\(' 'typeof\([^)]*\)\.[A-Za-z]' \
               'System\.Diagnostics\.Process' 'DllImport' 'Assembly\.'; do
  # strip comment lines - a doc comment naming the rule is not a violation
  if grep -rnE "$pattern" "$REPO/src" 2>/dev/null | grep -vE ':[0-9]+:[[:space:]]*(//|\*)'; then
    banned=1
  fi
done
if [ "$banned" = 1 ]; then
  echo "^^ these are rejected by PugMod's security verifier - the mod will not load" >&2
  exit 1
fi
echo "security lint: clean"
