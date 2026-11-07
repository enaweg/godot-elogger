#!/usr/bin/env bash
# Packages addons/eLogger the way the ePlugin updater installs it: the runtime sources hidden as .src again, so the
# plugin only compiles them once it is enabled, and zipped as the single release asset.
#
# Usage: package-addon.sh <output-zip> [version]
#   version  written into plugin.cfg, prerelease suffix included (1.2.0-beta.1). Omit it to keep plugin.cfg's version.
#
# Works on a copy, so the checkout is left as it is.
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $0 <output-zip> [version]" >&2
  exit 2
fi

output="$(realpath -m "$1")"
version="${2:-}"
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
addon="$repository/src/elogger/addons/eLogger"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/addons"
cp -r "$addon" "$stage/addons/"

if [ -n "$version" ]; then
  sed -i "s/version=\".*\"/version=\"$version\"/g" "$stage/addons/eLogger/plugin.cfg"
fi

# The dev project has the plugin enabled, so its sources are visible. Hide them again for distribution.
mv "$stage/addons/eLogger/src" "$stage/addons/eLogger/.src"

mkdir -p "$(dirname "$output")"
rm -f "$output"
(cd "$stage" && zip -qr "$output" addons/eLogger)
echo "Packaged $output"
