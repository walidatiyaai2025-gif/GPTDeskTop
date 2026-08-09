# GPTDeskTop Configuration Backup

## Purpose

The configuration backup is a portable, versioned JSON export for operator-controlled application settings and saved monitor definitions. It is intended for migration, safekeeping and controlled restore/import.

The current export/import schema is **1.0**.

## Export

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

## Export atomicity and failure behavior

The final destination is never written directly. GPTDeskTop creates a unique temporary file beside the selected destination, serializes the complete document, then atomically replaces/moves the temporary file to the final name. Temporary files are cleaned on failure.

The Settings dialog uses its existing busy guard while export is running, so Save and another backup operation cannot run concurrently.

## Export round-trip safety

A configuration backup is created only when every saved monitor has a stable ChatGPT `/c/...` conversation identity and no two saved monitors own the same logical conversation. Legacy invalid identities or canonical-equivalent duplicate owners must be repaired through **Runtime Health Repair** before export. GPTDeskTop never silently drops or merges monitors during export. Stable conversation URLs written to the portable backup are canonicalized. If validation fails, an existing destination backup is left unchanged and temporary export files are removed.

## Restore / import

Open **Settings > Backup & Portability** and choose **Import Configuration Backup**. GPTDeskTop first reads and validates the selected file without changing the database. The import accepts only schema **1.0** and uses strict JSON parsing: unknown top-level fields, unknown/duplicate setting keys, invalid setting ranges, invalid monitor values, duplicate monitor URLs inside the backup, and invalid monitor conversation identities are rejected before mutation.

A monitor URL must be an absolute HTTPS URL on a supported ChatGPT host and contain a real `/c/{conversation-id}` path segment. ChatGPT Home/New Chat pages, `/c/` without an ID, share-only URLs, non-HTTPS URLs, lookalike hosts and non-ChatGPT pages are not valid persisted monitor identities. Nested conversation paths such as `/g/{gpt-id}/c/{conversation-id}` and legacy `chat.openai.com/c/{conversation-id}` remain supported.

After validation, the operator receives a summary of how many settings and monitors are present and must explicitly confirm the import. **No** is the default confirmation choice.

The database apply phase uses one SQLite transaction:

- allowlisted settings present in the backup are upserted;
- a backup monitor whose canonical conversation identity matches one local monitor updates only operator-controlled monitor configuration while preserving the local stored URL spelling;
- that existing monitor keeps its local SQLite `Id`, runtime `TabId`, `RotationCount`, CreatedAt/history identity and existing Stored History;
- a backup monitor that does not exist locally is inserted with a new SQLite ID, an empty runtime `TabId`, and `RotationCount = 0`;
- local monitors that are absent from the backup are left untouched and are never deleted by import;
- if more than one local monitor owns the same logical conversation identity being imported, the import is considered ambiguous and the entire transaction is rolled back.

Import never reads or writes Stored History, ConversationRotations history, crash/recovery markers, runtime identities, UI layout state, exception-log contents, or machine/user identity from the backup.

## Restart requirement

A successful import changes persistent configuration while the running process may still hold live monitor/settings objects in memory. Restart **GPTDeskTop** after import before relying on the imported configuration. The import workflow does not restart the application automatically.

## Safety boundaries

Configuration import is a non-destructive merge, not a raw database restore. It does not replace the SQLite file, does not delete local monitors merely because they are absent from the backup, and does not import runtime/history state. This preserves the existing monitor/recovery/delivery architecture while allowing schema 1.0 backups to be moved between installations.