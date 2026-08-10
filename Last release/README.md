# Last release

This folder is managed automatically by the `Update Last release` GitHub Actions workflow.

After the publisher is merged and the current `main` commit passes the complete stable gate set, this folder will contain:

- `GPTDeskTop.exe` — latest verified Windows x64 Release application, self-contained and single-file.
- `RELEASE.txt` — source commit, version, generated time, validation receipt and SHA-256 checksum.
- `README.md` — this usage contract.

A newer build must never replace the executable until all eight required stable CI workflows pass for the same `main` source commit.
