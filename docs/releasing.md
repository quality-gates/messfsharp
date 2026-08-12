# Release and versioning

The package identifier and installed command are both `messfsharp`. The CLI and
structured reports read the assembly informational version, so the stable tag
is the release version source of truth.

## Release contract

A tag must match `vMAJOR.MINOR.PATCH` without leading zeroes. The release
workflow resolves the remote tag to its commit and builds only that commit. It
runs the complete test suite and self-analysis before creating release assets.

Each stable GitHub release contains exactly these files:

```text
messfsharp_VERSION_darwin_arm64.tar.gz
messfsharp_VERSION_darwin_amd64.tar.gz
checksums.txt
```

Each archive is a deterministic, self-contained .NET single-file publication.
`messfsharp` and `LICENSE` are its only top-level entries. The workflow builds
each archive once, verifies the archive contract, and runs those exact bytes on
matching Apple Silicon and Intel macOS runners. The smoke test checks the
version and analyzes a real fixture without using an installed .NET runtime.

After both smoke tests pass, the workflow creates a draft, uploads only the
verified assets, checks the server-side SHA-256 digests, and publishes the
release. Homebrew and NuGet publication cannot change the release asset set.
The workflow requires GitHub immutable releases to be enabled.

## Homebrew publication setup

1. Enable immutable releases for `quality-gates/messfsharp`.
2. Create an organization-owned GitHub App with only **Actions: write**. Install
   it only on `quality-gates/homebrew-tap` and do not add it to ruleset bypass
   lists.
3. Create a protected `homebrew` environment in this repository. Store
   `HOMEBREW_TAP_APP_ID` and `HOMEBREW_TAP_APP_PRIVATE_KEY` as environment
   secrets. Require review if appropriate, and allow stable tags plus the
   default branch used for manual retries.
4. Ensure `quality-gates/homebrew-tap` has the generic
   `publish-formula.yml` workflow on `main`, allows Actions to create pull
   requests, and protects formula updates with its required checks.

The source workflow sends only release identity, asset names, and certified
hashes. The tap-owned workflow treats those values as untrusted and verifies
the immutable release before generating a formula pull request.

## Normal release

Push the stable tag. The workflow validates and tests the source, builds and
smokes both macOS archives, publishes the immutable GitHub release, then:

- dispatches `quality-gates/homebrew-tap` through the protected environment and
  waits for formula publication;
- rebuilds, installs, and verifies the NuGet tool package, then publishes it
  when `NUGET_API_KEY` is provisioned.

## Recovery

Run the `Release` workflow manually with the existing tag. Retries do not
replace published bytes:

- matching draft assets are reused and only missing assets are uploaded;
- a differing draft asset or unexpected asset stops the workflow;
- an existing immutable release is downloaded, checked, and reused, so only
  downstream NuGet and Homebrew publication repeats;
- an existing published release that is not immutable stops the workflow;
- NuGet publication compares an existing package's stable payload before it
  accepts the retry.

Do not delete or edit a valid tag or immutable release to recover a downstream
failure. Correct the credentials, branch policy, tap workflow, or package
service problem, then retry the same tag.

For a local archive contract check, run:

```console
tests/release-archive-contract.sh
```
