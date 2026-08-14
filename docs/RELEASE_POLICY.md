# GPTDeskTop Release Policy

## Canonical branch

`main` is the canonical integration and release branch.

## Definition of a release

A release is **not complete** merely because the project compiles or an Actions artifact exists.

A completed GPTDeskTop release MUST satisfy all of the following:

1. The source commit is on `main`.
2. The Windows x64 Release build succeeds.
3. `Output/Setup/GPTDeskTop-Setup.exe` exists and is non-empty.
4. A distributable ZIP is produced from the setup output.
5. SHA-256 checksums are generated for the EXE and ZIP.
6. The build output is retained as a GitHub Actions artifact.
7. A real GitHub Release is published with downloadable assets:
   - `GPTDeskTop-Setup.exe`
   - `GPTDeskTop-Windows-x64.zip`
   - `SHA256SUMS.txt`
8. The GitHub Release targets the exact `main` commit that produced the binaries.

If any required asset is missing, the release job MUST fail and the release MUST NOT be reported as complete.

## Versioning

Until a manually curated semantic-version release is requested, verified `main` builds use immutable build tags in the form:

`v2.0.0-build.<github-run-number>`

Each tag points to the exact source commit used for that release. The newest successful build is marked as the latest GitHub Release.

## Security and provenance

- Release publication uses the workflow-scoped `GITHUB_TOKEN` with `contents: write` permission.
- No personal access token is stored in the repository for release publication.
- Checksums are generated on the GitHub-hosted Windows runner after packaging.
- Release assets must come from the same workflow run that built and verified the executable.

## Operator rule

Do not tell users that a new EXE release is ready until the GitHub Release exists and its required assets are visible/downloadable.
