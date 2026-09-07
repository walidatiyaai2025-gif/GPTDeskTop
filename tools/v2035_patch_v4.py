from pathlib import Path

source = Path("tools/v2035_patch_v3.py").read_text(encoding="utf-8")
exec(compile(source, "tools/v2035_patch_v3.py", "exec"))


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly 1 match, found {count}: {old!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

replace_once(
    "tests/GPTDeskTop.RuntimeTests/SimpleMonitorRateLimitSafetyRegressionTests.cs",
    'Assert.Contains("MinimumSendGap = TimeSpan.FromSeconds(15)", safety, StringComparison.Ordinal);',
    'Assert.Contains("MinimumSendGap = TimeSpan.FromSeconds(30)", safety, StringComparison.Ordinal);',
)

flight = "tests/GPTDeskTop.RuntimeTests/RuntimeFlightRecorderTests.cs"
replace_once(
    flight,
    "namespace GPTDeskTop.RuntimeTests;\n\npublic sealed class RuntimeFlightRecorderTests",
    "namespace GPTDeskTop.RuntimeTests;\n\n"
    "[CollectionDefinition(\"RuntimeFlightRecorder serial\", DisableParallelization = true)]\n"
    "public sealed class RuntimeFlightRecorderSerialCollection;\n\n"
    "[Collection(\"RuntimeFlightRecorder serial\")]\n"
    "public sealed class RuntimeFlightRecorderTests",
)

print("v2.0.35 pacing + deterministic test isolation patch applied")
