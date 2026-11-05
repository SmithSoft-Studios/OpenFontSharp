# Instructions for AI coding agents

These rules apply to every issue and pull request. They exist because earlier agent PRs passed their own tests while still being wrong or broken.

## Verification before you open or update a PR

1. **Run the tests in both configurations.** `dotnet test -c Debug` and `dotnet test -c Release`. Several code paths behave differently under `#if DEBUG`; a test that only passes in Release is a failing test. CI runs Release, so Debug failures will not be caught for you.
2. **Do not trust synthetic fixtures alone.** A hand-built font only proves the code agrees with your own reading of the specification. Where a real, permissively licensed font exercises the feature, also test against it (state the font and its licence in the PR). Fonts already in `src/OpenFontSharp.Tests/Resources` count.
3. **Check against an independent reader.** For anything that writes font data, load the output with a second, independent implementation and compare results glyph by glyph: outlines, advances, glyph counts, table checksums. A reader written by someone else (for example `fontTools`, FreeType, or the browser) is better evidence than our own reader accepting our own writer. Say in the PR which tool you used and what you compared.
4. **Make synthetic fixtures valid.** Build test data the way a real font would be built (operand counts, table order, checksums). Do not weaken a validity check to make a malformed fixture pass; fix the fixture.
5. **Test the failure paths.** Truncated data, counts larger than the data, offsets out of range, and recursion or loops must throw a documented exception (`OpenFontNotSupportedException`, `NotSupportedException`, `InvalidDataException`), never `IndexOutOfRangeException`, a hang, or an unbounded allocation.
6. **Test interactions.** If your change touches code another recent change also touches (subsetting, shaping, the reader), add a test that uses both features together.
7. **Review your own diff.** Before requesting review, read the full diff once as a reviewer would and fix what you find. Remove debug-only throws and stubs that can be reached in Release, or turn them into documented exceptions.

## Every change needs a test

- A new feature needs tests for its normal behaviour, its limits and its failure paths, in the same PR.
- A bug fix needs a regression test that fails without the fix.
- A behaviour change needs the old tests updated and a test that pins the new behaviour. Never delete or weaken an assertion to make a build pass; explain it in the PR.
- If something cannot be tested (no suitable font, no independent tool), say so in the PR description instead of leaving it out silently.

## What the PR description must say

- What was verified, how, and against what (tests, real fonts, independent tools).
- What was **not** verified or is unsupported. Do not claim more than the evidence shows.
- Any behaviour change, even a small one.

## Other rules

- Work from the specifications only. Do not copy code from other font libraries.
- Default behaviour must not change unless the issue says so; new behaviour is opt-in.
- Update `README.md`, `docs/COMPATIBILITY.md` and `CHANGELOG.md` in the same PR. Do not change the version or create tags.
- Preserve the existing line endings and BOMs.
- Keep issues, PRs and comments free of anything unrelated to fonts and the library.
