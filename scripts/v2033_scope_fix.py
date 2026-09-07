from pathlib import Path

# The hardening patch is now scoped correctly at source: the Monitor Only session
# constructs ChromeDevToolsService with browser-mutation recovery disabled, while
# legacy product paths retain their existing recovery behavior. Keep this helper
# as an explicit no-op because the one-shot workflow invokes it before the patch.
path = Path("scripts/v2033_hardening_patch.py")
if not path.exists():
    raise SystemExit("v2033 hardening patch script is missing")

print("v2.0.33 hardening is already scoped to Monitor Only; no pre-transform required.")
