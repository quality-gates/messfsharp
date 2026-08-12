#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <arm64|amd64> <runtime|machine|file-pattern>" >&2
  exit 64
fi

architecture="$1"
field="$2"
case "$architecture" in
  arm64)
    runtime="osx-arm64"
    machine="arm64"
    file_pattern='Mach-O 64-bit (arm64 executable|executable (arm64|ARM aarch64))'
    ;;
  amd64)
    runtime="osx-x64"
    machine="x86_64"
    file_pattern='Mach-O 64-bit (x86_64 executable|executable (x86_64|x86-64))'
    ;;
  *)
    echo "Architecture must be arm64 or amd64: $architecture" >&2
    exit 64
    ;;
esac

case "$field" in
  runtime) printf '%s\n' "$runtime" ;;
  machine) printf '%s\n' "$machine" ;;
  file-pattern) printf '%s\n' "$file_pattern" ;;
  *)
    echo "Field must be runtime, machine, or file-pattern: $field" >&2
    exit 64
    ;;
esac
