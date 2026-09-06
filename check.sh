#!/bin/bash
# Compile against the game's assemblies. PugMod compiles in-process at startup, so
# without this a typo first shows up in Player.log. Uses a throwaway csproj.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="${CK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Core Keeper}"
MANAGED="$GAME/CoreKeeper_Data/Managed"

[ -d "$MANAGED" ] || { echo "game assemblies not found at: $MANAGED" >&2
                       echo "Set CK_GAME_DIR to override." >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# Unity's mscorlib/netstandard must NOT be referenced: they shadow the SDK's core
# libs and every predefined type fails to resolve.
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
             Unity.Entities Unity.Mathematics Unity.Collections Unity.NetCode \
             Unity.Burst Unity.Transforms Unity.Networking.Transport \
             Pug.ECS.Components Pug.ECS.Extensions Pug.Objects \
             Interaction Interaction.Components; do
    [ -f "$MANAGED/$dll.dll" ] &&
      echo "    <Reference Include=\"$dll\"><HintPath>$MANAGED/$dll.dll</HintPath><Private>false</Private></Reference>"
  done
  printf '  </ItemGroup>\n</Project>\n'
} > "$WORK/check.csproj"

dotnet build "$WORK/check.csproj" -v q --nologo 2>&1 \
  | grep -E "error CS|warning CS|Build succeeded" \
  | sed "s|$REPO/||" \
  | sort -u

# PugMod's security verifier refuses anything touching these, and the only in-game
# symptom is "a mod failed to load: Compilation failed".
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
