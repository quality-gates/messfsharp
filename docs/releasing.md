# Release and versioning

The package identifier and installed command are both `messfsharp`. The CLI and
structured reports read the assembly informational version, so release
versioning has one source of truth.

Stable `vMAJOR.MINOR.PATCH` tags drive the verified build, pack,
isolated-install smoke test, checksum, and immutable GitHub release workflow.
NuGet publication is deliberately the final gated step and requires
repository-managed credentials.

For a packaged smoke test, install the `.nupkg` into an isolated tool location,
run `messfsharp --version`, then analyze a fixture in JSON or SARIF format.
