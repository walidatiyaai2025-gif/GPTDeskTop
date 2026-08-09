# Privacy-Safe Support Diagnostics Bundle

Issue: #33

## Purpose

GPTDeskTop can create a ZIP support bundle from the Runtime Health area for troubleshooting without exporting conversation content or copying the local SQLite database.

The launcher is visible only while Runtime Health is expanded, so it does not consume permanent workspace height.

## Included

`diagnostics.json` contains:

- GPTDeskTop version, .NET description, OS description and process architecture;
- sanitized Chrome/CDP configuration: loopback/remote classification, scheme and port only;
- sanitized start-URL classification (`ChatGPT`, `OtherHttps` or `Other`), never the configured host/path;
- monitoring timing values and database file name only;
- Chrome/CDP reachability, total controllable page count and ChatGPT tab count;
- SQLite reachability and saved/enabled/running monitor counts;
- counts of monitors with rotation/model routing enabled;
- up to 500 recent history rows reduced to direction/status aggregates and time range only;
- current exception-log file name, size and last-write time only;
- Healthy / Degraded / Unavailable runtime-health summary.

`README.txt` documents the bundle contents and privacy exclusions.

## Explicitly excluded

The bundle never exports:

- ChatGPT prompts or assistant responses;
- monitor titles, tab IDs or conversation URLs;
- auto-reply, handoff, timeout-recovery or new-chat message text;
- raw SQLite database contents;
- raw exception log contents;
- Windows user name, machine name or local profile paths.

## Reliability

- Chrome/CDP and SQLite collection share a five-second bounded probe window.
- Probe failures are represented by failure type only; exception messages are not copied into the bundle.
- Duplicate button presses are ignored while one bundle is being generated.
- The ZIP is written to a temporary file in the selected destination directory and atomically moved into place only after both entries are complete.
- Temporary output is deleted on failure or cancellation.
- Bundle generation does not start/stop monitors, send ChatGPT messages, create/reload/close Chrome tabs, or change SQLite settings.
