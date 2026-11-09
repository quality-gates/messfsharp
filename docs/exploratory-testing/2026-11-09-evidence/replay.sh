#!/usr/bin/env bash
set -u
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$PWD/.explore/sdk" PATH="$PWD/.explore/sdk:$PATH" HOME="$PWD/.explore/home" NUGET_PACKAGES="$PWD/.explore/nuget" DOTNET_CLI_HOME="$PWD/.explore/home" DOTNET_CLI_TELEMETRY_OPTOUT=1
rm -rf .explore/case
mkdir -p .explore/case/src .explore/case/reports
cd .explore/case
cli=(dotnet ../../src/MessFSharp/bin/Release/net10.0/messfsharp.dll)
printf 'module Demo\nlet mutable counter = 0\nlet increment () = counter <- counter + 1\n' > src/Good.fs
printf 'module Broken\nlet broken = (\n' > src/Bad.fs
printf 'sentinel\n' > reports/result.json
run() { printf '\n$'; printf ' %q' "${@}"; printf '\n'; "$@"; rc=$?; printf 'exit=%s\n' "$rc"; }
run "${cli[@]}" src/Good.fs json design --only GlobalVariable
run "${cli[@]}" src json design --only GlobalVariable --reportfile reports/result.json
printf '\nreport contents:\n'; python3 -m json.tool reports/result.json
run "${cli[@]}" src json design --only GlobalVariable --ignore-errors-on-exit --reportfile reports/result.json
run "${cli[@]}" src/Good.fs json design --only GlobalVariable --ignore-violations-on-exit
printf 'module Demo\nopen System.Diagnostics.CodeAnalysis\n[<SuppressMessage("messfsharp", "GlobalVariable")>]\nlet mutable counter = 0\nlet increment () = counter <- counter + 1\n' > src/Suppressed.fs
run "${cli[@]}" src/Suppressed.fs json design --only GlobalVariable
run "${cli[@]}" src/Suppressed.fs json design --only GlobalVariable --strict
printf 'module Demo\nlet extraordinaryLongVariableName = 1\n' > src/Names.fs
printf '<ruleset name="team"><rule name="LongVariable"><properties><property name="maximum" value="8" /></properties></rule></ruleset>\n' > team.xml
run "${cli[@]}" src/Names.fs json team.xml
printf '<ruleset name="team"><rule name="LongVariable"><properties><property name="maximum" value="40" /></properties></rule></ruleset>\n' > team.xml
run "${cli[@]}" src/Names.fs json team.xml
