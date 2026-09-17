# Release automation with release-please

**Date:** 2026-09-17
**Status:** Approved, pending implementation

## Purpose

Replace the manual tag-driven release process with release-please, so that every
version number in the repository is derived automatically from Conventional
Commit messages and no human ever types a version again.

The design is modelled on `atc-net/atc-rest-api-source-generator`, which already
runs release-please against a .NET solution.

## Problem

Three separate problems motivate the change.

**Releasing is manual.** Today a release requires pushing a `v*` tag by hand, or
running `release.yml` through `workflow_dispatch` and typing the version into a
form field. The version is then threaded through the workflow as
`-p:Version=` and `-p:ProductVersion=`.

**The declared version source does not exist.** `version.json` carries the
Nerdbank.GitVersioning schema and the value `1.0.6`, but Nerdbank.GitVersioning
is not referenced by any project in the solution. The last release was `v1.0.11`,
so the file has been stale for five releases. `README.md` and
`docs/architecture.md` both list "Versioning | Nerdbank.GitVersioning", which is
not what the repository does.

**Shipped builds report the wrong version.** No project sets `<Version>`, so
every assembly is stamped `1.0.0.0`. `ApplicationHelper.GetVersion()` reads that
value through `Atc.Helpers.AssemblyHelper.GetSystemVersion()`, which means the
Check-for-Updates dialog compares a real GitHub release against a hardcoded
`1.0.0` and reports an update as available regardless of what is installed.

## Decisions

These were settled before the design was written and are not revisited here.

| Decision | Choice |
|---|---|
| Version scope | One repository-wide version. All three shippables release together from one tag. |
| Pipeline shape | Full replacement. release-please is the only way to release; the `v*` tag trigger and `workflow_dispatch` are removed. |
| Version source of truth | `<Version>` in `Directory.Build.props`, maintained by release-please, mirrored by a `<ProductVersion>` in `setup/Directory.Build.props` for the reason given under Version flow. `version.json` is deleted. |
| NuGet authentication | The existing `NUGET_API_KEY` secret is carried over unchanged. Migrating to OIDC Trusted Publishing is explicitly deferred to a later step. |

## Version flow

The repository gets exactly two version records, both maintained by
release-please through its generic updater, which rewrites any value it finds
between `x-release-please-start-version` and `x-release-please-end` comments.

`Directory.Build.props` at the repository root:

```xml
<PropertyGroup Label="Versioning">
  <!-- x-release-please-start-version -->
  <Version>1.0.11</Version>
  <!-- x-release-please-end -->
</PropertyGroup>
```

`setup/Directory.Build.props`:

```xml
<PropertyGroup Label="Versioning">
  <!-- x-release-please-start-version -->
  <ProductVersion>1.0.11</ProductVersion>
  <!-- x-release-please-end -->
</PropertyGroup>
```

Two records are needed rather than one because `setup/Directory.Build.props`
shadows the root file. MSBuild stops at the nearest `Directory.Build.props`
walking up from a project, and unlike `src/Directory.Build.props`, the `setup`
file does not import its parent. This was confirmed empirically: querying
`OrganizationName` and `LangVersion` on
`setup/Linksoft.VideoSurveillance.Installer` returns empty strings for both. The
alternative — importing the root file into `setup/` — would drag the analyzer
package references back into the WiX builds, which is precisely what that file
exists to strip out.

With both records in place, nothing else needs to know a version:

- `dotnet build`, `publish` and `pack` read `<Version>` from the root props.
- The `.wixproj` files read `$(ProductVersion)` from the setup props. Their
  existing `Condition="'$(ProductVersion)' == ''"` fallback simply never fires.
  Neither `.wixproj` is modified.
- Assemblies are stamped correctly, so the Check-for-Updates comparison becomes
  meaningful.
- No workflow passes `-p:Version=` or `-p:ProductVersion=` anywhere.

## Configuration

`release-please-config.json`:

- `release-type: simple` — release-please has no native .NET type; `simple` plus
  `extra-files` is the standard approach and is what the reference repository
  uses.
- A single package at `"."` with
  `extra-files: ["Directory.Build.props", "setup/Directory.Build.props"]`.
- `bootstrap-sha: 04c46c540eb7a9c29d39cb41b4d0989cbb0f8281`, the commit tagged
  `v1.0.11`, so release-please does not scan history older than the last
  release.
- Changelog sections mapped to the commit types this repository actually uses:
  `feat` → New features, `fix(deps)` → Upgrades, `fix` → Bug fixes, `perf` →
  Performance improvements, `refactor` → Refactorings, `docs` → Documentation,
  `revert` → Reverts. `chore` and `style` stay hidden.

`.release-please-manifest.json` is `{".": "1.0.11"}`.

## Workflow

One workflow, `.github/workflows/release-please.yml`, triggered on push to
`main`. Four jobs:

1. **release-please** (ubuntu). Opens and maintains the release pull request. On
   merge it creates the tag and the GitHub Release from the changelog. Exposes
   `release_created`, `version` and `tag_name` as job outputs.
2. **publish-nuget** (windows). Packs `Linksoft.CameraWall.Wpf` and pushes it
   with `NUGET_API_KEY`.
3. **build-installers** (windows, matrix over the two applications). Publishes
   the app, downloads the FFmpeg shared build, and builds the MSI. The two
   near-identical jobs in today's `release.yml` collapse into one matrix.
4. **upload-assets** (ubuntu). Uploads both MSIs onto the release that
   release-please already created, using `gh release upload`.

Jobs 2 to 4 are gated on `needs.release-please.outputs.release_created`.

Keeping everything in one workflow run is deliberate. A GitHub Release created
by a workflow authenticated with `GITHUB_TOKEN` does not trigger downstream
workflows, so a design that split release creation and asset building across two
workflows would depend on the PAT for correctness rather than for convenience.

`ci.yml` and `pr-validation.yml` are not modified.

## Authentication

The release-please action uses a `RELEASE_PLEASE_PAT` secret: a fine-grained
personal access token with `contents: write` and `pull-requests: write`. The PAT
is not required for the release itself to work — `GITHUB_TOKEN` would also
create the pull request and the release — but a pull request opened by
`GITHUB_TOKEN` does not trigger workflows, so the release PR would get no CI
run. The workflow will carry a comment recording this, so the fallback is
discoverable if the secret is ever missing.

`NUGET_API_KEY` is used exactly as it is today.

## Files

| File | Action |
|---|---|
| `release-please-config.json` | new |
| `.release-please-manifest.json` | new |
| `.github/workflows/release-please.yml` | new |
| `.github/workflows/release.yml` | deleted |
| `Directory.Build.props` | `<Version>` block added |
| `setup/Directory.Build.props` | `<ProductVersion>` block added |
| `version.json` | deleted |
| `README.md` | versioning row corrected |
| `docs/architecture.md` | versioning row corrected |
| `CHANGELOG.md` | not created by hand; release-please creates it on the first release pull request |

## Trade-offs accepted

**No manual release path.** Removing both the `v*` tag trigger and
`workflow_dispatch` means a pushed tag builds nothing, and a broken release
asset cannot be re-cut for an existing version. The remedy is a follow-up commit
and a new version. This was chosen deliberately over keeping an escape hatch.

**Two version records instead of one.** Justified above by the MSBuild shadowing
behaviour. Both are maintained by release-please, so neither is edited by hand,
but they can in principle drift if someone edits one directly.

## Verification

A release cannot be proven end to end without cutting one. What will be verified
before the change is proposed for merge:

- `release-please-config.json` and `.release-please-manifest.json` validate
  against the published release-please schemas.
- The marker comments match the format the generic updater parses.
- `dotnet build` resolves `$(Version)` to `1.0.11` across the solution, queried
  with `dotnet msbuild -getProperty:Version`.
- `$(ProductVersion)` resolves to `1.0.11` on both `.wixproj` files, queried the
  same way — this is the check that catches the shadowing problem if the marker
  block is placed wrongly.
- The solution builds in Release with no warnings and the full test suite
  passes.
- `actionlint` over the new workflow, if available on this machine.

Anything that cannot be verified locally will be reported as unverified rather
than assumed.

## First release

There are 41 commits since `v1.0.11`: 14 `feat`, 10 `fix`, and no breaking
change markers. The first release pull request will therefore propose **1.1.0**
and generate a changelog covering all of them.

## Deferred

Migrating the NuGet push from the long-lived `NUGET_API_KEY` secret to OIDC
Trusted Publishing, as the reference repository does. This is a separate step,
taken after release-please is proven.
