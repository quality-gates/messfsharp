#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 4 ]]; then
  echo "Usage: $0 <archive> <version> <arm64|amd64> [--run]" >&2
  exit 64
fi

archive="$1"
version="$2"
architecture="$3"
run_archive="${4:-}"
expected_name="messfsharp_${version}_darwin_${architecture}.tar.gz"

if [[ "$(basename "$archive")" != "$expected_name" ]]; then
  echo "Archive must be named $expected_name" >&2
  exit 1
fi
if [[ "$run_archive" != "" && "$run_archive" != "--run" ]]; then
  echo "Fourth argument must be --run" >&2
  exit 64
fi

case "$architecture" in
  arm64)
    machine="arm64"
    ;;
  amd64)
    machine="x86_64"
    ;;
  *)
    echo "Architecture must be arm64 or amd64: $architecture" >&2
    exit 64
    ;;
esac

python3 - "$archive" <<'PY'
import pathlib
import sys
import tarfile

archive = pathlib.Path(sys.argv[1])
expected = [("LICENSE", 0o644), ("messfsharp", 0o755)]

with tarfile.open(archive, mode="r:gz") as release_archive:
    members = release_archive.getmembers()
    actual = [(member.name, member.mode) for member in members]
    if actual != expected:
        raise SystemExit(f"unexpected archive entries or modes: {actual!r}")
    for member in members:
        if not member.isfile():
            raise SystemExit(f"archive entry is not a regular file: {member.name}")
        if member.uid != 0 or member.gid != 0:
            raise SystemExit(f"archive entry has unstable ownership: {member.name}")
PY

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
tar -xzf "$archive" -C "$temporary_directory"
test -x "$temporary_directory/messfsharp"
test -s "$temporary_directory/LICENSE"
binary_description="$(file "$temporary_directory/messfsharp")"
case "$architecture" in
  arm64) grep -Eq 'Mach-O 64-bit (arm64 executable|executable (arm64|ARM aarch64))' <<<"$binary_description" ;;
  amd64) grep -Eq 'Mach-O 64-bit (x86_64 executable|executable (x86_64|x86-64))' <<<"$binary_description" ;;
esac

if [[ "$run_archive" == "--run" ]]; then
  test "$(uname -m)" = "$machine"
  repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  export DOTNET_MULTILEVEL_LOOKUP=0
  export DOTNET_ROOT="$temporary_directory/no-dotnet"
  test "$("$temporary_directory/messfsharp" --version)" = "messfsharp $version"
  "$temporary_directory/messfsharp" \
    "$repository_root/tests/Fixtures/clean.fs" \
    json fsharp \
    --reportfile "$temporary_directory/report.json"
  jq -e --arg version "$version" \
    '.tool == "messfsharp" and .version == $version and (.errors | length == 0)' \
    "$temporary_directory/report.json" >/dev/null
fi
