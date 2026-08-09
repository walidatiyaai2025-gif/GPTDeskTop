# GPTDeskTop Configuration Backup

## Purpose

The configuration backup is a portable, versioned JSON export for operator-controlled application settings and saved monitor definitions. It is intended for migration, safekeeping and the future restore/import workflow.

The current export schema is **1.0**.

## Where to create it

Open **Settings > Backup & Portability** and choose **Export Configuration Backup**. The operator selects the destination file. GPTDeskTop writes to a temporary file in the same directory and moves it into place only after the JSON has been written successfully.

## Sensitivity

This file is **not** the privacy-safe Support Bundle.

A configuration backup can contain:

- saved monitor titles;
- ChatGPT conversation URLs;
- auto-reply and new-chat/recovery message templates stored as operator configuration;
- monitor delays/timers and enabled state;
- conversation-rotation configuration;
- model-routing configuration;
- allowlisted DB-backed application settings.

Treat the file as sensitive operator data and store/share it accordingly.

## Explicit exclusions

Schema 1.0 intentionally excludes:

- Stored History and ConversationRotations history;
- raw SQLite database contents;
- runtime Chrome Tab IDs and SQLite monitor IDs;
- monitor rotation counters;
- crash/recovery/runtime state such as crash counters and pending recovery markers;
- UI layout and expansion state;
- exception-log contents;
- Windows user name, machine name and local profile identity;
- development-plan message catalog and schedule files.

The export is generated from an explicit application-setting allowlist. New SQLite keys are therefore not exported automatically.

## Atomicity and failure behavior

The final destination is never written directly. GPTDeskTop creates a unique temporary file beside the selected destination, serializes the complete document, then atomically replaces/moves the temporary file to the final name. Temporary files are cleaned on failure.

The Settings dialog uses its existing busy guard while export is running, so Save and a second export cannot run concurrently.

## Restore/import

Import/restore is intentionally not part of schema 1.0 implementation issue #35. The export contract is stabilized first so a later restore workflow can validate schema/version and apply configuration transactionally without importing runtime/history state.
