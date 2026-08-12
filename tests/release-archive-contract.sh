#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
version="1.2.3"
source_date_epoch="1700000000"

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64)
    architecture="arm64"
    run_argument="--run"
    ;;
  Darwin-x86_64)
    architecture="amd64"
    run_argument="--run"
    ;;
  *)
    architecture="amd64"
    run_argument=""
    ;;
esac

cd "$repository_root"
dotnet restore src/MessFSharp/MessFSharp.fsproj --locked-mode

scripts/build-macos-release-archive.sh \
  "$version" "$architecture" "$source_date_epoch" "$temporary_directory/first"
verify_arguments=(
  "$temporary_directory/first/messfsharp_${version}_darwin_${architecture}.tar.gz"
  "$version"
  "$architecture"
)
if [[ -n "$run_argument" ]]; then
  verify_arguments+=("$run_argument")
fi
scripts/verify-macos-release-archive.sh "${verify_arguments[@]}"

scripts/build-macos-release-archive.sh \
  "$version" "$architecture" "$source_date_epoch" "$temporary_directory/second"
cmp \
  "$temporary_directory/first/messfsharp_${version}_darwin_${architecture}.tar.gz" \
  "$temporary_directory/second/messfsharp_${version}_darwin_${architecture}.tar.gz"
