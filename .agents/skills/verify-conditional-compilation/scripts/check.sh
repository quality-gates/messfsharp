#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../../../.."
dotnet build src/MessFSharp/MessFSharp.fsproj -c Release --nologo --verbosity quiet -p:RestoreLockedMode=true
dotnet fsi --exec .agents/skills/verify-conditional-compilation/scripts/check.fsx "$@"
