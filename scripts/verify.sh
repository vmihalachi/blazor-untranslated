#!/usr/bin/env bash
# Runs the repository checks.
#
#   scripts/verify.sh          # versions agree, formatting, build and the fixture tests
#   scripts/verify.sh --fast   # versions agree and formatting only (seconds)
#
# The version lives in four places that must agree, and CHANGELOG.md needs its section: the UntranslatedVersion
# default in Directory.Build.props and, in site/index.html, the vX.Y.Z label and both PackageReference snippets.
set -uo pipefail
cd "$(dirname "$0")/.."

fast=0
[ "${1:-}" = "--fast" ] && fast=1
failed=0

versions_agree() {
  local found
  found="$( {
    sed -n 's/.*<UntranslatedVersion Condition=[^>]*>\([^<]*\)<.*/\1/p' Directory.Build.props
    grep -oE 'blob/main/CHANGELOG.md">v[0-9]+\.[0-9]+\.[0-9]+' site/index.html | grep -oE '[0-9]+\.[0-9]+\.[0-9]+'
    grep -oE 'Version(=|</span>=<span class="s">)"[0-9]+\.[0-9]+\.[0-9]+"' site/index.html | grep -oE '[0-9]+\.[0-9]+\.[0-9]+'
  } )"
  if [ "$(printf '%s\n' "$found" | grep -c .)" -ne 4 ]; then
    echo "expected 4 version strings (Directory.Build.props, the site label, two site snippets), found:"
    printf '%s\n' "$found" | sed 's/^/  /'; return 1
  fi
  if [ "$(printf '%s\n' "$found" | sort -u | wc -l)" -ne 1 ]; then
    echo "version strings disagree:"; printf '%s\n' "$found" | sort | uniq -c | sed 's/^/  /'; return 1
  fi
  local v; v="$(printf '%s\n' "$found" | head -n1)"
  grep -q "^## $v " CHANGELOG.md || { echo "CHANGELOG.md has no '## $v' section"; return 1; }
}

check() {
  local name="$1"; shift
  local log; log="$(mktemp)"
  if "$@" >"$log" 2>&1; then printf 'ok    %s\n' "$name"
  else printf 'FAIL  %s\n' "$name"; tail -n 30 "$log" | sed 's/^/      /'; failed=1; fi
  rm -f "$log"
}

check "versions agree" versions_agree
check "dotnet format" dotnet format --verify-no-changes
if [ "$fast" = 0 ]; then
  check "dotnet test (fixtures)" dotnet test
fi
exit "$failed"
