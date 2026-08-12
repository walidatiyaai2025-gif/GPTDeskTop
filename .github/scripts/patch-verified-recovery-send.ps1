$ErrorActionPreference = 'Stop'
$path = 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
$source = Get-Content $path -Raw

$old = 'public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)'
$new = 'public async Task<bool> SendChatMessageVerifiedAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default, bool requireNewTurn = false)'
if (-not $source.Contains($old)) { throw 'Verified send signature anchor not found.' }
$source = $source.Replace($old, $new)

$old = 'if (string.Equals(before.LastText, expected, StringComparison.Ordinal)) return true;'
$new = 'if (!requireNewTurn && string.Equals(before.LastText, expected, StringComparison.Ordinal)) return true;'
if (-not $source.Contains($old)) { throw 'Verified send idempotency anchor not found.' }
$source = $source.Replace($old, $new)

Set-Content -Path $path -Value $source -Encoding utf8NoBOM -NoNewline
Remove-Item '.github/workflows/agent-verified-recovery-send-patch.yml' -ErrorAction SilentlyContinue
Remove-Item '.github/scripts/patch-verified-recovery-send.ps1' -ErrorAction SilentlyContinue

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add -A
git commit -m 'Verify recreated monitor follow-up as a new turn'
git push origin HEAD:agent/recover-missing-monitor-tabs-shutdown-loading
