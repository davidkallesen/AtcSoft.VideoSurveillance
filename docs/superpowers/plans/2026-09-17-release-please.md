# Release-Please Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the manual tag-driven release process with release-please, so every version number in the repository is derived automatically from Conventional Commit messages.

**Architecture:** release-please runs on every push to `main`, maintaining a release pull request. Merging it creates the tag and GitHub Release, and gated jobs in the same workflow run then pack the NuGet package, build both MSIs and attach them to that release. The version lives in two MSBuild props files that release-please rewrites in place, so no build command ever passes a version.

**Tech Stack:** GitHub Actions, `googleapis/release-please-action` v5, MSBuild / .NET 10, WiX Toolset 5, PowerShell (Windows runners), `gh` CLI.

**Spec:** `docs/superpowers/specs/2026-09-17-release-please-design.md`

## Global Constraints

- Repository-wide single version. All three shippables release together from one tag.
- Version source of truth: `<Version>` in `Directory.Build.props` and `<ProductVersion>` in `setup/Directory.Build.props`, both wrapped in `x-release-please-start-version` / `x-release-please-end` comments.
- Starting version is exactly `1.0.11`. The manifest and both props files must agree on it.
- `bootstrap-sha` is exactly `04c46c540eb7a9c29d39cb41b4d0989cbb0f8281` (the commit tagged `v1.0.11`).
- No workflow may pass `-p:Version=` or `-p:ProductVersion=`. If a build needs a version, it comes from the props files.
- Neither `.wixproj` file is modified.
- NuGet push keeps using the existing `secrets.NUGET_API_KEY`. Do not introduce OIDC / Trusted Publishing — that is explicitly deferred.
- `ci.yml` and `pr-validation.yml` are not modified.
- Do not create `CHANGELOG.md` by hand. release-please creates it on the first release pull request.
- Existing workflows in this repo reference actions by tag (`actions/checkout@v4`), not by commit SHA. Follow that convention.
- Commit messages follow Conventional Commits, matching the existing history style.

---

### Task 1: Version source of truth

Move the version into MSBuild and delete the dead `version.json`. This is the task that makes versioning automatic; everything later just consumes it.

**Files:**
- Modify: `Directory.Build.props` (insert after the `Metadata configuration` PropertyGroup, which ends at line 7)
- Modify: `setup/Directory.Build.props` (insert a new PropertyGroup after the opening `<Project>` element)
- Delete: `version.json`

**Interfaces:**
- Consumes: nothing.
- Produces: MSBuild property `$(Version)` = `1.0.11` for every project under `src/` and `test/`; MSBuild property `$(ProductVersion)` = `1.0.11` for both `.wixproj` files. Task 2 lists both files as release-please `extra-files`. Task 3's workflow relies on both being set so it can omit version arguments.

- [ ] **Step 1: Record the current (broken) version behaviour**

Run:
```bash
dotnet msbuild src/Linksoft.CameraWall.Wpf/Linksoft.CameraWall.Wpf.csproj -getProperty:Version
dotnet msbuild setup/Linksoft.CameraWall.Installer/Linksoft.CameraWall.Installer.wixproj -getProperty:ProductVersion
```

Expected before the change: `Version` is `1.0.0` (the SDK default, because nothing sets it) and `ProductVersion` is `1.0.0` (the `.wixproj` fallback). Both are wrong, and Step 5 is what proves they became right.

- [ ] **Step 2: Add the version block to the root props**

In `Directory.Build.props`, insert immediately after the closing `</PropertyGroup>` of the `Metadata configuration` group:

```xml
  <!--
    The version is owned by release-please. It rewrites the value between the
    x-release-please markers when a release pull request is merged. Do not edit
    it by hand. See docs/superpowers/specs/2026-09-17-release-please-design.md
  -->
  <PropertyGroup Label="Versioning">
    <!-- x-release-please-start-version -->
    <Version>1.0.11</Version>
    <!-- x-release-please-end -->
  </PropertyGroup>
```

- [ ] **Step 3: Add the mirrored version block to the setup props**

In `setup/Directory.Build.props`, insert immediately after the opening `<Project ...>` element, before the analyzer PropertyGroup:

```xml
  <!--
    Mirrors <Version> from the root Directory.Build.props, because this file
    shadows it: MSBuild stops at the nearest Directory.Build.props, and unlike
    src/Directory.Build.props this one does not import its parent. Importing it
    would drag the root analyzer package references back into the WiX builds
    that this file exists to strip out.

    Also owned by release-please. Do not edit by hand.
  -->
  <PropertyGroup Label="Versioning">
    <!-- x-release-please-start-version -->
    <ProductVersion>1.0.11</ProductVersion>
    <!-- x-release-please-end -->
  </PropertyGroup>
```

- [ ] **Step 4: Delete the dead version file**

```bash
git rm version.json
```

- [ ] **Step 5: Verify both properties now resolve to 1.0.11**

Run:
```bash
dotnet msbuild src/Linksoft.CameraWall.Wpf/Linksoft.CameraWall.Wpf.csproj -getProperty:Version
dotnet msbuild setup/Linksoft.CameraWall.Installer/Linksoft.CameraWall.Installer.wixproj -getProperty:ProductVersion
dotnet msbuild setup/Linksoft.VideoSurveillance.Installer/Linksoft.VideoSurveillance.Installer.wixproj -getProperty:ProductVersion
```

Expected: all three print `1.0.11`.

If `ProductVersion` still prints `1.0.0`, the marker block landed in the wrong file or after the `.wixproj` already defaulted it — re-check that the block is inside `setup/Directory.Build.props`, not the root one. This check is the whole reason the setup props file is touched at all; do not proceed past a failure here.

- [ ] **Step 6: Verify the solution still builds and tests pass**

Run:
```bash
dotnet build --configuration Release --no-incremental
dotnet test --no-build --configuration Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` and `total: 407, failed: 0`. The build is run in Release because `TreatWarningsAsErrors` is only on in Release.

- [ ] **Step 7: Commit**

```bash
git add Directory.Build.props setup/Directory.Build.props version.json
git commit -m "build: move version into MSBuild props for release-please

- Add release-please-managed <Version> to Directory.Build.props
- Mirror it as <ProductVersion> in setup/Directory.Build.props, which
  shadows the root file and so cannot inherit it
- Delete version.json; Nerdbank.GitVersioning was never referenced and the
  file had been stale since v1.0.6"
```

---

### Task 2: release-please configuration

Add the two config files that tell release-please what to version and where to write it.

**Files:**
- Create: `release-please-config.json`
- Create: `.release-please-manifest.json`

**Interfaces:**
- Consumes: the two props files from Task 1, referenced by path in `extra-files`.
- Produces: config consumed by Task 3's workflow. release-please reads both files from the repository root by default, so the workflow needs no `config-file` or `manifest-file` input.

- [ ] **Step 1: Create the config file**

Create `release-please-config.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "release-type": "simple",
  "bootstrap-sha": "04c46c540eb7a9c29d39cb41b4d0989cbb0f8281",
  "pull-request-title-pattern": "chore: release version ${version}",
  "pull-request-header": ":robot: Preparing to release next version",
  "pull-request-footer": "This pull request was generated by release-please.",
  "packages": {
    ".": {
      "extra-files": [
        "Directory.Build.props",
        "setup/Directory.Build.props"
      ]
    }
  },
  "changelog-sections": [
    { "type": "feat", "section": "New features" },
    { "type": "fix", "scope": "deps", "section": "Upgrades" },
    { "type": "fix", "section": "Bug fixes" },
    { "type": "perf", "section": "Performance improvements" },
    { "type": "refactor", "section": "Refactorings" },
    { "type": "docs", "section": "Documentation" },
    { "type": "revert", "section": "Reverts" }
  ]
}
```

`release-type: simple` is correct here: release-please has no native .NET release type, so the version is carried by the generic updater against the marker comments. `chore` and `style` are absent from `changelog-sections` on purpose — they stay out of the changelog.

- [ ] **Step 2: Create the manifest**

Create `.release-please-manifest.json`:

```json
{
  ".": "1.0.11"
}
```

- [ ] **Step 3: Verify both files are valid JSON**

Run:
```bash
node -e "console.log(JSON.parse(require('fs').readFileSync('release-please-config.json','utf8')).packages['.'])"
node -e "console.log(JSON.parse(require('fs').readFileSync('.release-please-manifest.json','utf8')))"
```

Expected: the first prints an object containing `extra-files` with both props paths; the second prints `{ '.': '1.0.11' }`.

- [ ] **Step 4: Verify the manifest version matches the props files**

Run:
```bash
grep -n "<Version>" Directory.Build.props
grep -n "<ProductVersion>" setup/Directory.Build.props
grep -n "1.0.11" .release-please-manifest.json
```

Expected: all three show `1.0.11`. A mismatch here makes release-please compute the wrong next version, and nothing downstream would catch it.

- [ ] **Step 5: Dry-run release-please against the real repository**

Run:
```bash
npx --yes release-please release-pr \
  --repo-url=https://github.com/davidkallesen/Linksoft.VideoSurveillance \
  --token="$(gh auth token)" \
  --dry-run
```

Expected: output naming the next version as `1.1.0` and listing the changelog entries, with no pull request created. This is the strongest available check that the config parses and that `bootstrap-sha` is right.

If `gh auth token` is unavailable or the command cannot reach the network, skip this step and record it as unverified rather than claiming it passed. The remaining steps still stand on their own.

- [ ] **Step 6: Commit**

```bash
git add release-please-config.json .release-please-manifest.json
git commit -m "ci: add release-please configuration

- Single repo-wide package with both MSBuild props files as extra-files
- Bootstrap from the v1.0.11 commit so older history is not scanned
- Changelog sections mapped to the commit types this repo actually uses"
```

---

### Task 3: Replace the release workflow

Delete the tag-driven workflow and add the release-please workflow that supersedes it.

**Files:**
- Create: `.github/workflows/release-please.yml`
- Delete: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `release-please-config.json` and `.release-please-manifest.json` from Task 2; `$(Version)` and `$(ProductVersion)` from Task 1.
- Produces: job outputs `release_created` (string `'true'` when a release was cut), `tag_name` (e.g. `v1.1.0`) and `version` (e.g. `1.1.0`) from the `release-please` job, consumed by the three gated jobs below it.

- [ ] **Step 1: Create the workflow**

Create `.github/workflows/release-please.yml`:

```yaml
name: Release Please

on:
  push:
    branches: [main]

permissions:
  contents: write
  pull-requests: write

env:
  DOTNET_VERSION: '10.0.x'
  CONFIGURATION: Release

jobs:
  release-please:
    name: Release Please
    runs-on: ubuntu-latest
    outputs:
      release_created: ${{ steps.release.outputs.release_created }}
      tag_name: ${{ steps.release.outputs.tag_name }}
      version: ${{ steps.release.outputs.version }}

    steps:
      - name: Run release-please
        id: release
        uses: googleapis/release-please-action@v5
        with:
          # A PAT is used rather than GITHUB_TOKEN so the release pull request
          # triggers pr-validation.yml — events raised by GITHUB_TOKEN do not
          # trigger workflows. The fallback keeps releases working if the
          # secret is missing; the release PR then simply gets no CI run.
          token: ${{ secrets.RELEASE_PLEASE_PAT || secrets.GITHUB_TOKEN }}

  publish-nuget:
    name: Publish NuGet Package
    runs-on: windows-latest
    needs: release-please
    if: needs.release-please.outputs.release_created == 'true'

    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0
          ref: ${{ needs.release-please.outputs.tag_name }}

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore dependencies
        run: dotnet restore src/Linksoft.CameraWall.Wpf/Linksoft.CameraWall.Wpf.csproj

      # No -p:Version here: the version comes from Directory.Build.props,
      # which release-please stamped in the release commit.
      - name: Pack NuGet
        run: dotnet pack src/Linksoft.CameraWall.Wpf/Linksoft.CameraWall.Wpf.csproj --configuration ${{ env.CONFIGURATION }} --no-restore --output ./nuget -p:IsPackable=true

      - name: Push to NuGet.org
        run: |
          $packages = Get-ChildItem ./nuget -Filter *.nupkg
          if ($packages.Count -eq 0) { throw "No .nupkg files found in ./nuget" }
          foreach ($pkg in $packages) {
            Write-Host "Pushing $($pkg.Name)"
            dotnet nuget push $pkg.FullName --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
          }

  build-installers:
    name: Build MSI (${{ matrix.name }})
    runs-on: windows-latest
    needs: release-please
    if: needs.release-please.outputs.release_created == 'true'
    strategy:
      fail-fast: false
      matrix:
        include:
          - name: CameraWall
            app: src/Linksoft.CameraWall.Wpf.App/Linksoft.CameraWall.Wpf.App.csproj
            publish_dir: publish-camerawall
            installer: setup/Linksoft.CameraWall.Installer/Linksoft.CameraWall.Installer.wixproj
            installer_bin: setup/Linksoft.CameraWall.Installer/bin
            artifact: msi-installer-camerawall
          - name: VideoSurveillance
            app: src/Linksoft.VideoSurveillance.Wpf.App/Linksoft.VideoSurveillance.Wpf.App.csproj
            publish_dir: publish-videosurveillance
            installer: setup/Linksoft.VideoSurveillance.Installer/Linksoft.VideoSurveillance.Installer.wixproj
            installer_bin: setup/Linksoft.VideoSurveillance.Installer/bin
            artifact: msi-installer-videosurveillance

    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0
          ref: ${{ needs.release-please.outputs.tag_name }}

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Publish Application
        run: dotnet publish ${{ matrix.app }} --configuration ${{ env.CONFIGURATION }} --output ./${{ matrix.publish_dir }}

      - name: Download FFmpeg
        run: |
          $ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.0.1-full_build-shared.7z"
          Invoke-WebRequest -Uri $ffmpegUrl -OutFile ffmpeg.7z
          7z x ffmpeg.7z -offmpeg-temp
          $binDir = Get-ChildItem -Path ffmpeg-temp -Recurse -Directory -Filter "bin" | Select-Object -First 1
          New-Item -ItemType Directory -Path "./${{ matrix.publish_dir }}/ffmpeg" -Force
          Copy-Item "$($binDir.FullName)\*.dll" -Destination "./${{ matrix.publish_dir }}/ffmpeg/"

      # No -p:ProductVersion here: it comes from setup/Directory.Build.props.
      - name: Build Installer
        run: dotnet build ${{ matrix.installer }} --configuration ${{ env.CONFIGURATION }} -p:PublishDir=${{ github.workspace }}/${{ matrix.publish_dir }}

      - name: Upload MSI Artifact
        uses: actions/upload-artifact@v4
        with:
          name: ${{ matrix.artifact }}
          path: ${{ matrix.installer_bin }}/${{ env.CONFIGURATION }}/*.msi
          retention-days: 5

  upload-assets:
    name: Upload Release Assets
    runs-on: ubuntu-latest
    needs: [release-please, build-installers]
    if: needs.release-please.outputs.release_created == 'true'

    steps:
      - name: Download MSI artifacts
        uses: actions/download-artifact@v4
        with:
          pattern: msi-installer-*
          merge-multiple: true
          path: ./msi

      - name: Attach MSIs to the release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: gh release upload "${{ needs.release-please.outputs.tag_name }}" ./msi/*.msi --clobber --repo "${{ github.repository }}"
```

- [ ] **Step 2: Delete the superseded workflow**

```bash
git rm .github/workflows/release.yml
```

- [ ] **Step 3: Verify the workflow is valid YAML and has the expected shape**

Run:
```bash
node -e "
const fs=require('fs');
const t=fs.readFileSync('.github/workflows/release-please.yml','utf8');
if(/\t/.test(t)) throw new Error('tab character in YAML');
for (const j of ['release-please','publish-nuget','build-installers','upload-assets'])
  if(!t.includes('  '+j+':')) throw new Error('missing job: '+j);
if(t.includes('-p:Version=')||t.includes('-p:ProductVersion='))
  throw new Error('workflow must not pass a version');
console.log('workflow shape OK');
"
```

Expected: `workflow shape OK`.

- [ ] **Step 4: Run actionlint if available**

Run:
```bash
npx --yes actionlint .github/workflows/release-please.yml
```

Expected: no output (actionlint is silent on success). If the package cannot be fetched, record this step as unverified rather than passed — Step 3 already covers the structural checks that matter most.

- [ ] **Step 5: Confirm no other workflow still triggers on tags**

Run:
```bash
grep -rn "tags:" .github/workflows/ || echo "no tag triggers remain"
```

Expected: `no tag triggers remain`. A leftover `v*` trigger would race release-please and double-build a release.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/release-please.yml .github/workflows/release.yml
git commit -m "ci: replace tag-driven release with release-please workflow

- Merging the release PR now creates the tag, release and all assets
- Collapse the two near-identical MSI jobs into one matrix
- Drop the v* tag trigger and workflow_dispatch; release-please is the
  only release path
- No job passes a version; both come from the MSBuild props files"
```

---

### Task 4: Correct the versioning documentation

`README.md` and `docs/architecture.md` both claim the repository uses Nerdbank.GitVersioning, which was never true.

**Files:**
- Modify: `README.md:107`
- Modify: `docs/architecture.md:286`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Confirm the exact lines before editing**

Run:
```bash
grep -n "Nerdbank.GitVersioning" README.md docs/architecture.md
```

Expected: exactly two hits, `README.md:107` and `docs/architecture.md:286`, both of the form `| Versioning | Nerdbank.GitVersioning |`.

- [ ] **Step 2: Replace both**

```bash
sed -i 's/| Versioning | Nerdbank.GitVersioning |/| Versioning | release-please (Conventional Commits) |/' README.md docs/architecture.md
```

- [ ] **Step 3: Verify the claim is gone and the replacement landed**

Run:
```bash
grep -rn "Nerdbank" README.md docs/ || echo "no stale claims remain"
grep -n "| Versioning |" README.md docs/architecture.md
```

Expected: `no stale claims remain`, then two lines both reading `| Versioning | release-please (Conventional Commits) |`.

- [ ] **Step 4: Commit**

```bash
git add README.md docs/architecture.md
git commit -m "docs: correct the versioning row to release-please

Nerdbank.GitVersioning was never referenced by any project in the solution."
```

---

## Manual follow-up (not automatable)

These are for the repository owner and cannot be done from this workspace:

1. **Create the `RELEASE_PLEASE_PAT` secret** — a fine-grained personal access token scoped to this repository with `contents: write` and `pull-requests: write`. Without it the workflow still runs via the `GITHUB_TOKEN` fallback, but the release pull request will not trigger `pr-validation.yml`.
2. **Merge to `main`** — the first push to `main` opens a release pull request proposing `1.1.0`. Review its changelog before merging; merging it is what cuts the release.
3. **Confirm `NUGET_API_KEY` is still valid** — it is reused unchanged, but it has not been exercised since `v1.0.11`.

## What cannot be verified before merge

A release cannot be proven end to end without cutting one. Specifically unverifiable here: that release-please rewrites both marker blocks correctly, that `gh release upload` attaches to a release created in the same run, and that the `secrets.X || secrets.Y` token fallback resolves as intended. The first release is the test; the `--dry-run` in Task 2 Step 5 is the closest available proxy.
