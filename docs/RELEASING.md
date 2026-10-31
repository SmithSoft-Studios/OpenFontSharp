# Releasing

OpenFontSharp ships as one NuGet package. TrinePDF depends on it, so release OpenFontSharp first, then update the submodule and version in TrinePDF.

## Cut a release

1. Move the `[Unreleased]` entries in `CHANGELOG.md` under a new version heading.
2. Set `<Version>` in `src/OpenFontSharp/OpenFontSharp.csproj` (follow [SemVer](https://semver.org): new public API bumps the minor, a breaking change the major).
3. Merge that change to `main` through a pull request.
4. After the merge, switch to `main` and pull its latest changes (`git switch main`, then `git pull --ff-only origin main`).
5. Confirm the project version is `1.2.0`, then create an annotated tag: `git tag -a v1.2.0 -m "OpenFontSharp 1.2.0"`.
6. Push the tag: `git push origin v1.2.0`. This starts the publishing workflow; do not push the tag while only preparing the release.

The release workflow refuses to publish unless the tag equals `<Version>`, the tests pass and `.github/scripts/check-packages.sh` can install the package into a clean project and subset a font with it. The package check also requires the XML API documentation under `lib/net10.0`. Tags containing `-` become GitHub pre-releases. A failed release can be re-run; a version already on nuget.org is skipped.

Before choosing a version for the nullable cleanup, review the [source-compatibility notice](COMPATIBILITY.md) and include it in the release notes. The CLR signatures are unchanged, but consumers treating nullable warnings as errors may need new presence guards.

Check the package locally first with `bash .github/scripts/check-packages.sh` (needs network access).

## One-time setup

- GitHub environment **release** (Settings → Environments), limited to the tag pattern `v*`.
- Secret **NUGET_USER**: the nuget.org account name that owns the package.
- nuget.org → Trusted Publishing → add a policy: repository owner `SmithSoft-Studios`, repository `OpenFontSharp`, workflow file `release.yml`, environment `release`, package `OpenFontSharp`, scope: push new package versions.

No API key is stored. If trusted publishing is unavailable, set a `NUGET_API_KEY` secret instead; the workflow uses it when present.
