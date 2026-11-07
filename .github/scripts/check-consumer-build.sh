#!/usr/bin/env bash
# Builds a fresh consumer project from a packaged eLogger release, so a release that does not compile for its users
# fails here instead of in their projects. The dev project cannot catch that: it has the plugin enabled, carries the
# test packages and only builds against the Godot version it pins.
#
# Every Godot version is built in the two states a user's project goes through, each in the editor configuration and
# in both configurations of an exported game (which compile without TOOLS, so editor-only ePlugin code is absent):
#   installed  the release just copied in: sources hidden as .src, no ZLogger packages yet
#   enabled    after ePlugin enabled the plugin: sources visible, the recipe's packages added
#
# Usage: check-consumer-build.sh <eLogger-zip> <godot-version>...
set -euo pipefail

if [ $# -lt 2 ]; then
  echo "usage: $0 <eLogger-zip> <godot-version>..." >&2
  exit 2
fi

package="$(realpath "$1")"
shift
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fail() {
  echo "::error::$*" >&2
  exit 1
}

build_all() {
  local project="$1" state="$2"
  for configuration in Debug ExportDebug ExportRelease; do
    echo "::group::Godot $godot, $state, $configuration"
    dotnet build "$project/Consumer.csproj" --configuration "$configuration" -nologo \
      || fail "eLogger does not build for Godot $godot ($state, $configuration)"
    echo "::endgroup::"
  done
}

for godot in "$@"; do
  [ -n "$godot" ] || fail "empty Godot version; pass versions such as 4.5.0"
  project="$work/godot-$godot"
  mkdir -p "$project"
  unzip -q "$package" -d "$project"

  # The package must be what the ePlugin updater expects to install.
  [ -d "$project/addons/eLogger/.src" ] || fail "the package does not hide the runtime sources as addons/eLogger/.src"
  [ ! -e "$project/addons/eLogger/src" ] || fail "the package ships addons/eLogger/src, which compiles before the plugin is enabled"
  grep -q '^update_url=' "$project/addons/eLogger/plugin.cfg" || fail "plugin.cfg has no update_url; the updater needs it"

  # Users install ePlugin themselves; the vendored copy is the version eLogger is developed against.
  cp -r "$repository/src/elogger/addons/ePlugin" "$project/addons/"

  cat > "$project/Consumer.csproj" <<EOF
<Project Sdk="Godot.NET.Sdk/$godot">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
EOF

  # Installed: only ELoggerPlugin.cs is visible, and it must compile with nothing but Godot and ePlugin.
  build_all "$project" "installed"

  # Enabled: what ePlugin does for ELoggerPlugin.CreateRecipe. Keep this in sync with the recipe. Like the recipe,
  # take the latest package versions, so a breaking ZLogger or ZString release shows up here first.
  mv "$project/addons/eLogger/.src" "$project/addons/eLogger/src"
  dotnet add "$project/Consumer.csproj" package ZLogger
  dotnet add "$project/Consumer.csproj" package ZString
  build_all "$project" "enabled"
done

echo "eLogger builds for Godot $* in every state and configuration."
