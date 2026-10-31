# Releasing

OpenFontSharp ships as one NuGet package. TrinePDF depends on it, so release OpenFontSharp first, then update the submodule and version in TrinePDF.

## Cut a release

1. Move the `[Unreleased]` entries in `CHANGELOG.md` under a new version heading.
2. Set `<Version>` in `src/OpenFontSharp/OpenFontSharp.csproj` (follow [SemVer](https://semver.org): new public API bumps the minor, a breaking change the major).
3. Merge that change to `main` through a pull request.
4. Tag and push: `git tag v1.1.0 && git push origin v1.1.0`.

The release workflow refuses to publish unless the tag equals `<Version>`, the tests pass and `.github/scripts/check-packages.sh` can install the package into a clean project and subset a font with it. Tags containing `-` become GitHub pre-releases. A failed release can be re-run; a version already on nuget.org is skipped.

Check the package locally first with `bash .github/scripts/check-packages.sh` (needs network access).

## One-time setup

- GitHub environment **release** (Settings → Environments), limited to the tag pattern `v*`.
- Secret **NUGET_USER**: the nuget.org account name that owns the package.
- nuget.org → Trusted Publishing → add a policy: repository owner `SmithSoft-Studios`, repository `OpenFontSharp`, workflow file `release.yml`, environment `release`, package `OpenFontSharp`, scope: push new package versions.

No API key is stored. If trusted publishing is unavailable, set a `NUGET_API_KEY` secret instead; the workflow uses it when present.
