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
  for dll in UnityEngine.CoreModule UnityEngine.UI UnityEngine.UIModule \
             UnityEngine.TextRenderingModule UnityEngine.InputLegacyModule \
             UnityEngine.IMGUIModule Unity.TextMeshPro \
             UnityEngine.TextCoreFontEngineModule UnityEngine.TextCoreTextEngineModule \
             PugMod.SDK.Runtime PugMod.SDK \
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
