#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <arm64|amd64> <source-date-epoch> <output-directory>" >&2
  exit 64
fi

version="$1"
architecture="$2"
source_date_epoch="$3"
output_directory="$4"

if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Version must match MAJOR.MINOR.PATCH: $version" >&2
  exit 64
fi
if [[ ! "$source_date_epoch" =~ ^[0-9]+$ ]]; then
  echo "Source date epoch must be a non-negative integer: $source_date_epoch" >&2
  exit 64
fi

case "$architecture" in
  arm64) runtime_identifier="osx-arm64" ;;
  amd64) runtime_identifier="osx-x64" ;;
  *)
    echo "Architecture must be arm64 or amd64: $architecture" >&2
    exit 64
    ;;
esac

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repository_root/src/MessFSharp/MessFSharp.fsproj"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
publish_directory="$temporary_directory/publish"
staging_directory="$temporary_directory/package"
archive_name="messfsharp_${version}_darwin_${architecture}.tar.gz"
archive="$output_directory/$archive_name"

mkdir -p "$publish_directory" "$staging_directory" "$output_directory"
dotnet restore "$project" \
  --runtime "$runtime_identifier" \
  --locked-mode \
  -p:NuGetLockFilePath="packages.$runtime_identifier.lock.json"
dotnet publish "$project" \
  --configuration Release \
  --runtime "$runtime_identifier" \
  --self-contained true \
  --no-restore \
  --output "$publish_directory" \
  -p:PackageVersion="$version" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugSymbols=false \
  -p:DebugType=None

install -m 0755 "$publish_directory/messfsharp" "$staging_directory/messfsharp"
install -m 0644 "$repository_root/LICENSE" "$staging_directory/LICENSE"

python3 - "$staging_directory" "$archive" "$source_date_epoch" <<'PY'
import gzip
import pathlib
import sys
import tarfile

staging = pathlib.Path(sys.argv[1])
archive = pathlib.Path(sys.argv[2])
mtime = int(sys.argv[3])

with archive.open("wb") as raw_archive:
    with gzip.GzipFile(filename="", mode="wb", fileobj=raw_archive, mtime=mtime) as compressed_archive:
        with tarfile.open(fileobj=compressed_archive, mode="w", format=tarfile.USTAR_FORMAT) as tar_archive:
            for name, mode in (("LICENSE", 0o644), ("messfsharp", 0o755)):
                source = staging / name
                info = tar_archive.gettarinfo(str(source), arcname=name)
                info.uid = 0
                info.gid = 0
                info.uname = ""
                info.gname = ""
                info.mtime = mtime
                info.mode = mode
                with source.open("rb") as contents:
                    tar_archive.addfile(info, contents)
PY

printf '%s\n' "$archive"
