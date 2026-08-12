$ErrorActionPreference = 'Stop'
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git fetch origin main
$mergeOutput = git merge origin/main --no-edit 2>&1
$mergeExit = $LASTEXITCODE
$mergeOutput | ForEach-Object { Write-Host $_ }
if ($mergeExit -ne 0) {
    Write-Host '--- MERGE STATUS ---'
    git status --short
    exit $mergeExit
}
Remove-Item '.github/workflows/agent-sync-latest-main.yml' -ErrorAction SilentlyContinue
Remove-Item '.github/scripts/sync-latest-main.ps1' -ErrorAction SilentlyContinue
git add -A
git commit --amend --no-edit
git push origin HEAD:agent/recover-missing-monitor-tabs-shutdown-loading
