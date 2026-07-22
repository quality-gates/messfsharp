#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <candidate.nupkg> <published.nupkg>" >&2
  exit 64
fi

candidate="$1"
published="$2"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

candidate_entries="$temporary_directory/candidate-entries.txt"
published_entries="$temporary_directory/published-entries.txt"

stable_entries() {
  unzip -Z1 "$1" \
    | awk '!/^(_rels\/\.rels|package\/services\/metadata\/core-properties\/.*\.psmdcp|\.signature\.p7s)$/' \
    | LC_ALL=C sort
}

# NuGet assigns a fresh core-properties ID on each pack and writes it into the
# package relationship file. Those archive-generation details are not package
# payload, so compare every stable entry by literal name and contents.
stable_entries "$candidate" > "$candidate_entries"
stable_entries "$published" > "$published_entries"
cmp "$candidate_entries" "$published_entries"

while IFS= read -r entry; do
  # unzip treats its file arguments as patterns, even when shell-quoted.
  literal_entry="$entry"
  literal_entry="${literal_entry//\\/\\\\}"
  literal_entry="${literal_entry//\[/\\[}"
  literal_entry="${literal_entry//\]/\\]}"
  literal_entry="${literal_entry//\*/\\*}"
  literal_entry="${literal_entry//\?/\\?}"
  cmp <(unzip -p "$candidate" "$literal_entry") <(unzip -p "$published" "$literal_entry")
done < "$candidate_entries"
