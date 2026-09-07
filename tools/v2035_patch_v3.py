from pathlib import Path

source = Path("tools/v2035_patch_v2.py").read_text(encoding="utf-8")
old = "            var microBreakUntil = _state.MicroBreakUntilUtc is { } existing && existing > now ? existing : null;"
new = "            DateTimeOffset? microBreakUntil = _state.MicroBreakUntilUtc is { } existing && existing > now ? existing : null;"
if source.count(old) != 1:
    raise SystemExit(f"Expected exactly one nullable pacing line, found {source.count(old)}")
source = source.replace(old, new, 1)
exec(compile(source, "tools/v2035_patch_v2.py", "exec"))
