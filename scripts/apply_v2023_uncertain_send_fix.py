from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
runner_path = ROOT / "src/GPTDeskTop/Services/SimpleMonitorRunner.cs"
test_path = ROOT / "tests/GPTDeskTop.RuntimeTests/SimpleMonitorRateLimitSafetyRegressionTests.cs"

text = runner_path.read_text(encoding="utf-8")
old = '''                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    if (await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false))
                    {
                        SetStatus("RATE LIMITED — physical submit was rejected. Safe backoff completed; retry remains behind the global send gate.", "RateLimited");
                        continue;
                    }
                    throw;
                }
'''
new = '''                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var rateLimited = false;
                    try
                    {
                        rateLimited = await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // The send outcome is already uncertain. A failed diagnostic probe must never
                        // convert uncertainty into permission to try the composer again.
                    }

                    if (rateLimited)
                    {
                        SetStatus("RATE LIMITED — physical send outcome is uncertain. Breaker is active and automatic retry is blocked to prevent duplicate delivery.", "RateLimited");
                        throw new SimpleMonitorBlockedException(
                            "ChatGPT rate limited the profile while the physical send outcome was uncertain. Automatic retry is blocked to prevent a duplicate. Wait for the cooldown, inspect the same chat, then press Start only after reconciling whether the message arrived.");
                    }

                    throw new SimpleMonitorBlockedException(
                        $"The physical send outcome is uncertain ({ex.Message}). Automatic retry is blocked to prevent duplicate delivery. Inspect the same chat before pressing Start again.");
                }
'''
if text.count(old) != 1:
    raise SystemExit(f"Expected one uncertain-send catch block, found {text.count(old)}")
text = text.replace(old, new, 1)
text = text.replace("            ThrowIfUnsafe(before);", "                ThrowIfUnsafe(before);")
text = text.replace("            ThrowIfUnsafe(recheck);", "                ThrowIfUnsafe(recheck);")
runner_path.write_text(text, encoding="utf-8")

test = test_path.read_text(encoding="utf-8")
needle = '''        Assert.Contains("No physical send yet", safety, StringComparison.Ordinal);
    }
}'''
replacement = '''        Assert.Contains("No physical send yet", safety, StringComparison.Ordinal);
        Assert.Contains("physical send outcome is uncertain", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic retry is blocked to prevent duplicate", runner, StringComparison.OrdinalIgnoreCase);

        var uncertainStart = runner.IndexOf("catch (Exception ex) when (!cancellationToken.IsCancellationRequested)", StringComparison.Ordinal);
        var uncertainEnd = runner.IndexOf("if (!sent)", uncertainStart, StringComparison.Ordinal);
        Assert.True(uncertainStart >= 0 && uncertainEnd > uncertainStart);
        var uncertainBlock = runner[uncertainStart..uncertainEnd];
        Assert.DoesNotContain("continue;", uncertainBlock, StringComparison.Ordinal);
    }
}'''
if test.count(needle) != 1:
    raise SystemExit("Could not locate rate-limit safety test tail")
test_path.write_text(test.replace(needle, replacement, 1), encoding="utf-8")

print("uncertain-send retry path is fail-closed")
