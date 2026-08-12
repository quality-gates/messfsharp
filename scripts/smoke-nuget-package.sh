#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <package-directory> <version> <working-directory>" >&2
  exit 64
fi

package_directory="$(cd "$1" && pwd)"
version="$2"
working_directory="$3"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package="$package_directory/messfsharp.${version}.nupkg"

unzip -p "$package" messfsharp.nuspec > "$working_directory/messfsharp.nuspec"
grep -F '<id>messfsharp</id>' "$working_directory/messfsharp.nuspec"
grep -F "<version>${version}</version>" "$working_directory/messfsharp.nuspec"
grep -F '<license type="expression">MIT</license>' "$working_directory/messfsharp.nuspec"

mkdir -p "$working_directory/messfsharp-tool"
cat > "$working_directory/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release" value="$package_directory" />
  </packageSources>
</configuration>
EOF

dotnet tool install \
  --tool-path "$working_directory/messfsharp-tool" \
  --configfile "$working_directory/nuget.config" \
  messfsharp --version "$version"
test "$("$working_directory/messfsharp-tool/messfsharp" --version)" = "messfsharp $version"
"$working_directory/messfsharp-tool/messfsharp" \
  "$repository_root/tests/Fixtures/clean.fs" json fsharp \
  --reportfile "$working_directory/smoke.json"
jq -e --arg version "$version" \
  '.tool == "messfsharp" and .version == $version and (.errors | length == 0)' \
  "$working_directory/smoke.json" >/dev/null
