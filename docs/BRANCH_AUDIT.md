# Branch audit and main reconciliation

Date: 2026-08-14

## Policy
`main` is the canonical source for releasable GPTDeskTop binaries. Historical branch refs are not release sources and are never bulk-merged merely because Git ancestry reports them as ahead/diverged.

Because this repository commonly uses squash merges, branch ancestry alone is not proof that work is missing. Audit decisions are based on the logical PR/patch history and current `main` contents.

## Audit method
1. Inventory repository branches and open/closed pull requests.
2. Treat merged PRs as logically reconciled even when their old branch commits are not ancestors of `main` after squash merge.
3. Review closed-unmerged PRs for explicit superseded/duplicate disposition.
4. Inspect open PRs against current `main` and port only still-useful deltas from stale branches.
5. Inspect no-PR snapshot branches for canonical successors; never import stale branch history blindly.
6. Require the full project CI set before merging reconciled work.

## Real missing work found

### GHINT-021 — branch pagination
Historical branch: `agent/ghint-021-paginate-branches` / PR #265.

The current GitHub UI previously stopped branch discovery at the first 100 results. This repository has more than 100 branch refs, so valid branches such as `main` could be omitted depending on API order. PR #265 passed the full CI set and was squash-merged into `main` during this audit.

### UI-026 — single compact dashboard header owner
Historical branch: `codex/single-owner-compact-header` / PR #245.

The useful UI delta was still absent from current `main`, but the historical branch was hundreds of commits behind. The audit therefore ported only the isolated useful change onto a fresh branch from current `main`, validated it with the full CI set, and merged reconciliation PR #266. PR #245 was then closed as superseded by the reconciled implementation.

## Closed-unmerged PR review
The closed-unmerged PR set was reviewed. The remaining entries were explicitly superseded or duplicate work rather than releasable missing deltas, including the old Phase 1 branch, historical schedule/recovery variants, the pre-PERF implementation-plan branch, a duplicate PR, and an older UI-stability implementation later replaced by canonical work.

## No-PR historical snapshots
Several branch refs are intermediate snapshots with canonical successors and must not be bulk-merged. Examples include:
- `agent/ui-live-monitor-recovery-v2-pre221` → later canonical recovery work.
- `agent/monitor-stability-50` → superseded by `agent/monitor-stability-50-clean` / merged PR #239.
- `agent/fix-cdp-outbound-recovery` → superseded by `agent/fix-cdp-outbound-recovery-v2` / merged PR #260.
- diagnostic/stability scratch branches whose useful work was incorporated by later canonical PRs.

These refs may remain for history, but they are not release inputs.

## Binary source policy
The stable portable `GPTDeskTop.exe` is generated only by `.github/workflows/update-last-release.yml` from the current `main` commit. The workflow:
- checks out `main` explicitly;
- rejects stale source SHAs if `main` advanced;
- requires all eight stable gates green for the exact same `main` SHA;
- publishes a self-contained, single-file Windows x64 `GPTDeskTop.exe` with a stable build identity;
- records SHA-256 and source commit in `Last release/RELEASE.txt`;
- uploads the verified portable EXE as the `GPTDeskTop-Portable-main` Actions artifact;
- updates `Last release/GPTDeskTop.exe` only after verification.

Setup/ZIP GitHub Releases are also produced from the `main` Release Artifact workflow. Branch builds are CI candidates only and must not be presented as the stable application.

## Result
The audit reconciles real missing work instead of forcing every historical ref into `main`. Old/superseded branch refs can continue to exist without affecting the release lineage. `main` remains the single canonical source of stable executable output.
