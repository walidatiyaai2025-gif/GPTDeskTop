using GPTDeskTop.Models;
using GPTDeskTop.Services;
using Microsoft.Data.Sqlite;

namespace GPTDeskTop.Data;

public sealed record ConfigurationImportDatabaseResult(int SettingsApplied, int MonitorsUpdated, int MonitorsInserted);
public sealed record ConfigurationBackupDatabaseSnapshot(IReadOnlyDictionary<string, string?> Settings, IReadOnlyList<SavedMonitor> Monitors);
public sealed record MonitorRegistrationResult(long MonitorId, bool Created);
public sealed record MonitorConversationRebindDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl);
public sealed record MonitorConversationHandoffDatabaseResult(long MonitorId, string PreviousUrl, string NewUrl, int RotationCount, string Title);

public sealed class LocalDatabase
{
    private readonly string _connectionString;

    public LocalDatabase(string fileName)
    {
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(AppContext.BaseDirectory, fileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, DefaultTimeout = 5 }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS MessageLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    Prompt TEXT NOT NULL DEFAULT '',
                    Response TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS SavedMonitors (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TabId TEXT NOT NULL DEFAULT '', Title TEXT NOT NULL DEFAULT '', Url TEXT NOT NULL DEFAULT '',
                    AutoReply TEXT NOT NULL DEFAULT '', ReplyDelaySeconds INTEGER NOT NULL DEFAULT 3, TimerSeconds INTEGER NOT NULL DEFAULT 1,
                    Enabled INTEGER NOT NULL DEFAULT 1, ConversationRotationEnabled INTEGER NOT NULL DEFAULT 1,
                    NewChatStartMessage TEXT NOT NULL DEFAULT 'كمل', NewChatDelaySeconds INTEGER NOT NULL DEFAULT 30,
                    RotationCooldownSeconds INTEGER NOT NULL DEFAULT 60, MaxConversationRotations INTEGER NOT NULL DEFAULT 0,
                    RotationCount INTEGER NOT NULL DEFAULT 0, ModelRoutingEnabled INTEGER NOT NULL DEFAULT 0,
                    PreferredModel TEXT NOT NULL DEFAULT 'Auto', FallbackModel TEXT NOT NULL DEFAULT 'Auto',
                    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ConversationRotations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, MonitorId INTEGER NOT NULL,
                    OldTabId TEXT NOT NULL DEFAULT '', NewTabId TEXT NOT NULL DEFAULT '', Trigger TEXT NOT NULL DEFAULT '',
                    StartMessage TEXT NOT NULL DEFAULT '', Timestamp TEXT NOT NULL,
                    FOREIGN KEY(MonitorId) REFERENCES SavedMonitors(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Url ON SavedMonitors(Url);
                CREATE INDEX IF NOT EXISTS IX_SavedMonitors_Enabled ON SavedMonitors(Enabled);
                CREATE INDEX IF NOT EXISTS IX_ConversationRotations_MonitorId ON ConversationRotations(MonitorId);
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('MonitorMode', 'ChromeCDP');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultAutoReply', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMonitorDelaySeconds', '3');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMonitorTimerSeconds', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultConversationRotationEnabled', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultNewChatStartMessage', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultNewChatDelaySeconds', '30');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultRotationCooldownSeconds', '60');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultMaxConversationRotations', '0');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultModelRoutingEnabled', '0');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultPreferredModel', 'Auto');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('DefaultFallbackModel', 'Auto');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('HandoffEnabled', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('HandoffMaxChars', '7000');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('TimeoutRecoveryMessage', 'كمل');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationDurationSeconds', '8');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationSoundEnabled', '1');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('NotificationSoundType', 'Asterisk');
                INSERT OR IGNORE INTO AppSettings(Key, Value) VALUES ('ChromeHidden', '0');
                """;
            await command.ExecuteNonQueryAsync();
        }
        await EnsureColumnAsync(connection, "MessageLogs", "MonitorId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "MessageLogs", "TabId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "MessageLogs", "TabTitle", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "SavedMonitors", "ReplyDelaySeconds", "INTEGER NOT NULL DEFAULT 3");
        await EnsureColumnAsync(connection, "SavedMonitors", "TimerSeconds", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "SavedMonitors", "ConversationRotationEnabled", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "SavedMonitors", "NewChatStartMessage", "TEXT NOT NULL DEFAULT 'كمل'");
        await EnsureColumnAsync(connection, "SavedMonitors", "NewChatDelaySeconds", "INTEGER NOT NULL DEFAULT 30");
        await EnsureColumnAsync(connection, "SavedMonitors", "RotationCooldownSeconds", "INTEGER NOT NULL DEFAULT 60");
        await EnsureColumnAsync(connection, "SavedMonitors", "MaxConversationRotations", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "SavedMonitors", "RotationCount", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "SavedMonitors", "ModelRoutingEnabled", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "SavedMonitors", "PreferredModel", "TEXT NOT NULL DEFAULT 'Auto'");
        await EnsureColumnAsync(connection, "SavedMonitors", "FallbackModel", "TEXT NOT NULL DEFAULT 'Auto'");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var check = connection.CreateCommand(); check.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};"; await alter.ExecuteNonQueryAsync();
    }

    public async Task<long> SaveMonitorAsync(SavedMonitor monitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Id <= 0)
            return (await RegisterMonitorIfConversationAvailableAsync(monitor, cancellationToken)).MonitorId;

        var now = DateTime.UtcNow;
        ClampMonitorSettings(monitor);
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SavedMonitors SET TabId=$tabId,Title=$title,Url=$url,AutoReply=$autoReply,ReplyDelaySeconds=$replyDelay,TimerSeconds=$timer,Enabled=$enabled,
            ConversationRotationEnabled=$rotationEnabled,NewChatStartMessage=$message,NewChatDelaySeconds=$newChatDelay,RotationCooldownSeconds=$cooldown,
            MaxConversationRotations=$maxRotations,RotationCount=$rotationCount,ModelRoutingEnabled=$modelRouting,PreferredModel=$preferredModel,FallbackModel=$fallbackModel,UpdatedAt=$updatedAt WHERE Id=$id; SELECT $id;
            """;
        command.Parameters.AddWithValue("$id", monitor.Id);
        AddMonitorParameters(command, monitor, now, includeCreatedAt: false);
        monitor.Id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)); monitor.UpdatedAt = now.ToLocalTime(); if (monitor.CreatedAt == default) monitor.CreatedAt = now.ToLocalTime(); return monitor.Id;
    }

    public async Task<bool> UpdateMonitorConfigurationAsync(
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Id <= 0)
            throw new InvalidOperationException("Monitor configuration can only be updated after the monitor is saved.");

        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SavedMonitors SET
                AutoReply=$autoReply,
                ReplyDelaySeconds=$replyDelay,
                TimerSeconds=$timer,
                Enabled=$enabled,
                ConversationRotationEnabled=$rotationEnabled,
                NewChatStartMessage=$message,
                NewChatDelaySeconds=$newChatDelay,
                RotationCooldownSeconds=$cooldown,
                MaxConversationRotations=$maxRotations,
                ModelRoutingEnabled=$modelRouting,
                PreferredModel=$preferredModel,
                FallbackModel=$fallbackModel,
                UpdatedAt=$updatedAt
            WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$id", monitor.Id);
        command.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
        command.Parameters.AddWithValue("$replyDelay", Math.Clamp(monitor.ReplyDelaySeconds, 0, 300));
        command.Parameters.AddWithValue("$timer", Math.Clamp(monitor.TimerSeconds, 1, 60));
        command.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$message", monitor.NewChatStartMessage ?? string.Empty);
        command.Parameters.AddWithValue("$newChatDelay", Math.Clamp(monitor.NewChatDelaySeconds, 0, 600));
        command.Parameters.AddWithValue("$cooldown", Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600));
        command.Parameters.AddWithValue("$maxRotations", Math.Clamp(monitor.MaxConversationRotations, 0, 1000));
        command.Parameters.AddWithValue("$modelRouting", monitor.ModelRoutingEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$preferredModel", string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel);
        command.Parameters.AddWithValue("$fallbackModel", string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> UpdateMonitorRuntimeTargetIfConversationMatchesAsync(
        long monitorId,
        string expectedConversationUrl,
        string targetTabId,
        string targetTitle,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(expectedConversationUrl))
            throw new InvalidOperationException("The saved monitor conversation identity is required.");
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The resolved Chrome target ID is required.");

        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            string? currentUrl = null;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                currentUrl = Convert.ToString(await load.ExecuteScalarAsync(cancellationToken));
            }
            if (string.IsNullOrWhiteSpace(currentUrl) || !ConversationIdentityMatches(currentUrl, expectedConversationUrl))
            {
                transaction.Rollback();
                return false;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, UpdatedAt=$updatedAt WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", monitorId);
            command.Parameters.AddWithValue("$tabId", targetTabId);
            command.Parameters.AddWithValue("$title", targetTitle ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            transaction.Commit();
            return updated;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task<MonitorRegistrationResult> RegisterMonitorIfConversationAvailableAsync(
        SavedMonitor monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Id > 0)
            throw new InvalidOperationException("Duplicate-safe monitor registration is only valid for a new monitor.");
        if (string.IsNullOrWhiteSpace(monitor.Url))
            throw new InvalidOperationException("A conversation URL is required to register a monitor.");

        ClampMonitorSettings(monitor);
        if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            monitor.Url = ChatGptConversationIdentity.Normalize(monitor.Url);
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))
            {
                var existingId = await FindLogicalConversationOwnerIdAsync(connection, transaction, monitor.Url, excludeMonitorId: null, cancellationToken);
                if (existingId.HasValue)
                {
                    transaction.Commit();
                    monitor.Id = existingId.Value;
                    return new MonitorRegistrationResult(existingId.Value, false);
                }
            }
            else
            {
                await using var find = connection.CreateCommand();
                find.Transaction = transaction;
                find.CommandText = "SELECT Id FROM SavedMonitors WHERE Url=$url COLLATE NOCASE ORDER BY Id LIMIT 1;";
                find.Parameters.AddWithValue("$url", monitor.Url);
                var existing = await find.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && existing is not DBNull)
                {
                    var existingId = Convert.ToInt64(existing);
                    transaction.Commit();
                    monitor.Id = existingId;
                    return new MonitorRegistrationResult(existingId, false);
                }
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO SavedMonitors(TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt)
                VALUES($tabId,$title,$url,$autoReply,$replyDelay,$timer,$enabled,$rotationEnabled,$message,$newChatDelay,$cooldown,$maxRotations,$rotationCount,$modelRouting,$preferredModel,$fallbackModel,$createdAt,$updatedAt); SELECT last_insert_rowid();
                """;
            AddMonitorParameters(insert, monitor, now, includeCreatedAt: true);
            var monitorId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            transaction.Commit();

            monitor.Id = monitorId;
            monitor.CreatedAt = now.ToLocalTime();
            monitor.UpdatedAt = now.ToLocalTime();
            return new MonitorRegistrationResult(monitorId, true);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task<MonitorConversationRebindDatabaseResult> RebindMonitorConversationIfAvailableAsync(
        long monitorId,
        string expectedCurrentUrl,
        string targetTabId,
        string targetTitle,
        string targetUrl,
        bool requireDuplicateSourceOwnership,
        string diagnosticPrompt,
        string diagnosticResponse,
        string diagnosticStatus,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The selected ChatGPT conversation does not have a usable Chrome target ID.");
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new InvalidOperationException("A target conversation URL is required for monitor repair.");

        expectedCurrentUrl ??= string.Empty;
        targetTitle ??= string.Empty;
        diagnosticPrompt ??= string.Empty;
        diagnosticResponse ??= string.Empty;
        diagnosticStatus ??= string.Empty;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            string currentUrl;
            string currentTitle;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url, Title FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                await using var reader = await load.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");
                currentUrl = reader.GetString(0);
                currentTitle = reader.GetString(1);
            }

            if (!ConversationIdentityMatches(currentUrl, expectedCurrentUrl))
                throw new InvalidOperationException("Saved monitor conversation identity changed before repair could be applied. Refresh and try again.");

            var canonicalTargetUrl = NormalizeStableConversationUrl(targetUrl);
            if (ChatGptConversationIdentity.IsSame(currentUrl, canonicalTargetUrl))
                throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve conversation ownership.");

            if (requireDuplicateSourceOwnership)
            {
                var sourceOwnerCount = await CountLogicalConversationOwnersAsync(connection, transaction, currentUrl, cancellationToken);
                if (sourceOwnerCount < 2)
                    throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");
            }

            var existingOwner = await FindLogicalConversationOwnerIdAsync(connection, transaction, canonicalTargetUrl, monitorId, cancellationToken);
            if (existingOwner.HasValue)
                throw new InvalidOperationException($"Monitor #{existingOwner.Value} already owns the selected ChatGPT conversation.");

            var now = DateTime.UtcNow.ToString("O");
            var appliedTitle = string.IsNullOrWhiteSpace(targetTitle) ? currentTitle : targetTitle;
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, Url=$url, UpdatedAt=$updatedAt WHERE Id=$id;";
                update.Parameters.AddWithValue("$id", monitorId);
                update.Parameters.AddWithValue("$tabId", targetTabId);
                update.Parameters.AddWithValue("$title", appliedTitle);
                update.Parameters.AddWithValue("$url", canonicalTargetUrl);
                update.Parameters.AddWithValue("$updatedAt", now);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Saved monitor #{monitorId} could not be updated.");
            }

            await using (var insertLog = connection.CreateCommand())
            {
                insertLog.Transaction = transaction;
                insertLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,$dir,$p,$r,$s);";
                insertLog.Parameters.AddWithValue("$ts", now);
                insertLog.Parameters.AddWithValue("$m", monitorId);
                insertLog.Parameters.AddWithValue("$id", targetTabId);
                insertLog.Parameters.AddWithValue("$title", appliedTitle);
                insertLog.Parameters.AddWithValue("$dir", "System");
                insertLog.Parameters.AddWithValue("$p", diagnosticPrompt);
                insertLog.Parameters.AddWithValue("$r", diagnosticResponse);
                insertLog.Parameters.AddWithValue("$s", diagnosticStatus);
                await insertLog.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new MonitorConversationRebindDatabaseResult(monitorId, currentUrl, canonicalTargetUrl);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task<MonitorConversationHandoffDatabaseResult> CommitMonitorConversationHandoffAsync(
        long monitorId,
        string expectedCurrentUrl,
        string targetTabId,
        string targetTitle,
        string targetUrl,
        bool incrementRotationCount,
        bool recordRotation,
        string oldTabId,
        string rotationTrigger,
        string startMessage,
        string triggerResponse,
        string successStatus,
        string outboundStatus,
        CancellationToken cancellationToken = default)
    {
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (string.IsNullOrWhiteSpace(expectedCurrentUrl))
            throw new InvalidOperationException("The current monitor conversation identity is required for handoff.");
        if (string.IsNullOrWhiteSpace(targetTabId))
            throw new InvalidOperationException("The new conversation Chrome target ID is required for handoff.");
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new InvalidOperationException("The new stable conversation URL is required for handoff.");
        if (ChatGptConversationIdentity.IsSame(expectedCurrentUrl, targetUrl))
            throw new InvalidOperationException("Intentional handoff requires a different target conversation.");

        targetTitle ??= string.Empty;
        oldTabId ??= string.Empty;
        rotationTrigger ??= string.Empty;
        startMessage ??= string.Empty;
        triggerResponse ??= string.Empty;
        successStatus ??= string.Empty;
        outboundStatus ??= string.Empty;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            string currentUrl;
            string currentTitle;
            int currentRotationCount;
            await using (var load = connection.CreateCommand())
            {
                load.Transaction = transaction;
                load.CommandText = "SELECT Url, Title, RotationCount FROM SavedMonitors WHERE Id=$id LIMIT 1;";
                load.Parameters.AddWithValue("$id", monitorId);
                await using var reader = await load.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");
                currentUrl = reader.GetString(0);
                currentTitle = reader.GetString(1);
                currentRotationCount = reader.GetInt32(2);
            }

            if (!ConversationIdentityMatches(currentUrl, expectedCurrentUrl))
                throw new InvalidOperationException("Saved monitor conversation identity changed before intentional handoff could be committed.");

            var canonicalTargetUrl = NormalizeStableConversationUrl(targetUrl);
            if (ChatGptConversationIdentity.IsSame(currentUrl, canonicalTargetUrl))
                throw new InvalidOperationException("Intentional handoff requires a different target conversation.");
            var existingOwner = await FindLogicalConversationOwnerIdAsync(connection, transaction, canonicalTargetUrl, monitorId, cancellationToken);
            if (existingOwner.HasValue)
                throw new InvalidOperationException($"Monitor #{existingOwner.Value} already owns the intentional handoff target conversation.");

            var nextRotationCount = incrementRotationCount ? checked(currentRotationCount + 1) : currentRotationCount;
            var appliedTitle = string.IsNullOrWhiteSpace(targetTitle) ? currentTitle : targetTitle;
            var now = DateTime.UtcNow.ToString("O");

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE SavedMonitors SET TabId=$tabId, Title=$title, Url=$url, RotationCount=$rotationCount, UpdatedAt=$updatedAt WHERE Id=$id;";
                update.Parameters.AddWithValue("$id", monitorId);
                update.Parameters.AddWithValue("$tabId", targetTabId);
                update.Parameters.AddWithValue("$title", appliedTitle);
                update.Parameters.AddWithValue("$url", canonicalTargetUrl);
                update.Parameters.AddWithValue("$rotationCount", nextRotationCount);
                update.Parameters.AddWithValue("$updatedAt", now);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Saved monitor #{monitorId} could not be moved to the new conversation.");
            }

            if (recordRotation)
            {
                await using var rotation = connection.CreateCommand();
                rotation.Transaction = transaction;
                rotation.CommandText = "INSERT INTO ConversationRotations(MonitorId,OldTabId,NewTabId,Trigger,StartMessage,Timestamp) VALUES($m,$o,$n,$t,$s,$ts);";
                rotation.Parameters.AddWithValue("$m", monitorId);
                rotation.Parameters.AddWithValue("$o", oldTabId);
                rotation.Parameters.AddWithValue("$n", targetTabId);
                rotation.Parameters.AddWithValue("$t", rotationTrigger);
                rotation.Parameters.AddWithValue("$s", startMessage);
                rotation.Parameters.AddWithValue("$ts", now);
                await rotation.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var systemLog = connection.CreateCommand())
            {
                systemLog.Transaction = transaction;
                systemLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,'System',$p,$r,$s);";
                systemLog.Parameters.AddWithValue("$ts", now);
                systemLog.Parameters.AddWithValue("$m", monitorId);
                systemLog.Parameters.AddWithValue("$id", targetTabId);
                systemLog.Parameters.AddWithValue("$title", appliedTitle);
                systemLog.Parameters.AddWithValue("$p", startMessage);
                systemLog.Parameters.AddWithValue("$r", triggerResponse);
                systemLog.Parameters.AddWithValue("$s", successStatus);
                await systemLog.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var outboundLog = connection.CreateCommand())
            {
                outboundLog.Transaction = transaction;
                outboundLog.CommandText = "INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,'Outbound',$p,'',$s);";
                outboundLog.Parameters.AddWithValue("$ts", now);
                outboundLog.Parameters.AddWithValue("$m", monitorId);
                outboundLog.Parameters.AddWithValue("$id", targetTabId);
                outboundLog.Parameters.AddWithValue("$title", appliedTitle);
                outboundLog.Parameters.AddWithValue("$p", startMessage);
                outboundLog.Parameters.AddWithValue("$s", outboundStatus);
                await outboundLog.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new MonitorConversationHandoffDatabaseResult(
                monitorId,
                currentUrl,
                canonicalTargetUrl,
                nextRotationCount,
                appliedTitle);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static string NormalizeStableConversationUrl(string url)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(url))
            throw new InvalidOperationException("A stable ChatGPT conversation URL is required.");
        return ChatGptConversationIdentity.Normalize(url);
    }

    private static bool ConversationIdentityMatches(string currentUrl, string expectedUrl)
    {
        var currentStable = RuntimeHealthPresentation.IsChatGptConversationUrl(currentUrl);
        var expectedStable = RuntimeHealthPresentation.IsChatGptConversationUrl(expectedUrl);
        return currentStable && expectedStable
            ? ChatGptConversationIdentity.IsSame(currentUrl, expectedUrl)
            : string.Equals(currentUrl, expectedUrl, StringComparison.Ordinal);
    }

    private static async Task<long?> FindLogicalConversationOwnerIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        long? excludeMonitorId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = excludeMonitorId.HasValue
            ? "SELECT Id, Url FROM SavedMonitors WHERE Id<>$id ORDER BY Id;"
            : "SELECT Id, Url FROM SavedMonitors ORDER BY Id;";
        if (excludeMonitorId.HasValue)
            command.Parameters.AddWithValue("$id", excludeMonitorId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var url = reader.GetString(1);
            if (ChatGptConversationIdentity.IsSame(url, targetUrl))
                return id;
        }
        return null;
    }

    private static async Task<int> CountLogicalConversationOwnersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        CancellationToken cancellationToken)
    {
        var count = 0;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Url FROM SavedMonitors;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (ChatGptConversationIdentity.IsSame(reader.GetString(0), targetUrl))
                count++;
        }
        return count;
    }

    private static void ClampMonitorSettings(SavedMonitor monitor)
    {
        monitor.ReplyDelaySeconds = Math.Clamp(monitor.ReplyDelaySeconds, 0, 300);
        monitor.TimerSeconds = Math.Clamp(monitor.TimerSeconds, 1, 60);
        monitor.NewChatDelaySeconds = Math.Clamp(monitor.NewChatDelaySeconds, 0, 600);
        monitor.RotationCooldownSeconds = Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600);
        monitor.MaxConversationRotations = Math.Clamp(monitor.MaxConversationRotations, 0, 1000);
    }

    private static void AddMonitorParameters(SqliteCommand command, SavedMonitor monitor, DateTime now, bool includeCreatedAt)
    {
        command.Parameters.AddWithValue("$tabId", monitor.TabId ?? ""); command.Parameters.AddWithValue("$title", monitor.Title ?? ""); command.Parameters.AddWithValue("$url", monitor.Url ?? "");
        command.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? ""); command.Parameters.AddWithValue("$replyDelay", monitor.ReplyDelaySeconds); command.Parameters.AddWithValue("$timer", monitor.TimerSeconds);
        command.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0); command.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$message", monitor.NewChatStartMessage ?? "كمل"); command.Parameters.AddWithValue("$newChatDelay", monitor.NewChatDelaySeconds);
        command.Parameters.AddWithValue("$cooldown", monitor.RotationCooldownSeconds); command.Parameters.AddWithValue("$maxRotations", monitor.MaxConversationRotations); command.Parameters.AddWithValue("$rotationCount", monitor.RotationCount);
        command.Parameters.AddWithValue("$modelRouting", monitor.ModelRoutingEnabled ? 1 : 0); command.Parameters.AddWithValue("$preferredModel", monitor.PreferredModel ?? "Auto"); command.Parameters.AddWithValue("$fallbackModel", monitor.FallbackModel ?? "Auto");
        if (includeCreatedAt) command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
    }

    public async Task<ConfigurationBackupDatabaseSnapshot> ReadConfigurationBackupSnapshotAsync(
        IReadOnlyList<string> settingKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingKeys);
        var keys = settingKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var settings = keys.ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        var monitors = new List<SavedMonitor>();

        var snapshotConnectionString = new SqliteConnectionStringBuilder(_connectionString)
            { Cache = SqliteCacheMode.Private }
            .ToString();
        await using var connection = new SqliteConnection(snapshotConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            if (keys.Length > 0)
            {
                await using var settingsCommand = connection.CreateCommand();
                settingsCommand.Transaction = transaction;
                var parameterNames = new string[keys.Length];
                for (var index = 0; index < keys.Length; index++)
                {
                    parameterNames[index] = $"$key{index}";
                    settingsCommand.Parameters.AddWithValue(parameterNames[index], keys[index]);
                }
                settingsCommand.CommandText = $"SELECT Key, Value FROM AppSettings WHERE Key IN ({string.Join(',', parameterNames)});";
                await using var settingsReader = await settingsCommand.ExecuteReaderAsync(cancellationToken);
                while (await settingsReader.ReadAsync(cancellationToken))
                    settings[settingsReader.GetString(0)] = settingsReader.GetString(1);
            }

            await using var monitorsCommand = connection.CreateCommand();
            monitorsCommand.Transaction = transaction;
            monitorsCommand.CommandText = "SELECT Id,TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt FROM SavedMonitors ORDER BY Id;";
            await using var monitorReader = await monitorsCommand.ExecuteReaderAsync(cancellationToken);
            while (await monitorReader.ReadAsync(cancellationToken))
            {
                monitors.Add(new SavedMonitor
                {
                    Id = monitorReader.GetInt64(0),
                    TabId = monitorReader.GetString(1),
                    Title = monitorReader.GetString(2),
                    Url = monitorReader.GetString(3),
                    AutoReply = monitorReader.GetString(4),
                    ReplyDelaySeconds = Math.Clamp(monitorReader.GetInt32(5), 0, 300),
                    TimerSeconds = Math.Clamp(monitorReader.GetInt32(6), 1, 60),
                    Enabled = monitorReader.GetInt64(7) != 0,
                    ConversationRotationEnabled = monitorReader.GetInt64(8) != 0,
                    NewChatStartMessage = monitorReader.GetString(9),
                    NewChatDelaySeconds = Math.Clamp(monitorReader.GetInt32(10), 0, 600),
                    RotationCooldownSeconds = Math.Clamp(monitorReader.GetInt32(11), 0, 3600),
                    MaxConversationRotations = Math.Clamp(monitorReader.GetInt32(12), 0, 1000),
                    RotationCount = Math.Max(0, monitorReader.GetInt32(13)),
                    ModelRoutingEnabled = monitorReader.GetInt64(14) != 0,
                    PreferredModel = monitorReader.GetString(15),
                    FallbackModel = monitorReader.GetString(16),
                    CreatedAt = ParseLocal(monitorReader.GetString(17)),
                    UpdatedAt = ParseLocal(monitorReader.GetString(18))
                });
            }

            transaction.Commit();
            return new ConfigurationBackupDatabaseSnapshot(settings, monitors);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task<List<SavedMonitor>> GetSavedMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SavedMonitor>(); await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt FROM SavedMonitors ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new SavedMonitor {
            Id=reader.GetInt64(0),TabId=reader.GetString(1),Title=reader.GetString(2),Url=reader.GetString(3),AutoReply=reader.GetString(4),ReplyDelaySeconds=Math.Clamp(reader.GetInt32(5),0,300),TimerSeconds=Math.Clamp(reader.GetInt32(6),1,60),Enabled=reader.GetInt64(7)!=0,
            ConversationRotationEnabled=reader.GetInt64(8)!=0,NewChatStartMessage=reader.GetString(9),NewChatDelaySeconds=Math.Clamp(reader.GetInt32(10),0,600),RotationCooldownSeconds=Math.Clamp(reader.GetInt32(11),0,3600),MaxConversationRotations=Math.Clamp(reader.GetInt32(12),0,1000),RotationCount=Math.Max(0,reader.GetInt32(13)),
            ModelRoutingEnabled=reader.GetInt64(14)!=0,PreferredModel=reader.GetString(15),FallbackModel=reader.GetString(16),CreatedAt=ParseLocal(reader.GetString(17)),UpdatedAt=ParseLocal(reader.GetString(18))
        });
        return result;
    }

    public async Task<ConfigurationImportDatabaseResult> ApplyConfigurationImportAsync(
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyList<SavedMonitor> monitors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitors);

        // Defend the persistence boundary even when a caller bypasses CreatePlan.
        // This validation intentionally happens before opening a transaction or writing settings.
        var canonicalImportUrls = new string[monitors.Count];
        var importedConversationIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index] ?? throw new InvalidOperationException("The configuration import contains a null monitor entry.");
            var canonicalUrl = NormalizeStableConversationUrl(monitor.Url ?? string.Empty);
            if (!importedConversationIdentities.Add(canonicalUrl))
                throw new InvalidOperationException($"Configuration import contains the same logical ChatGPT conversation more than once: '{canonicalUrl}'.");
            canonicalImportUrls[index] = canonicalUrl;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);

        var settingsApplied = 0;
        var monitorsUpdated = 0;
        var monitorsInserted = 0;
        var now = DateTime.UtcNow.ToString("O");

        try
        {
            foreach (var pair in settings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var settingCommand = connection.CreateCommand();
                settingCommand.Transaction = transaction;
                settingCommand.CommandText = "INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
                settingCommand.Parameters.AddWithValue("$key", pair.Key);
                settingCommand.Parameters.AddWithValue("$value", pair.Value ?? string.Empty);
                await settingCommand.ExecuteNonQueryAsync(cancellationToken);
                settingsApplied++;
            }

            for (var index = 0; index < monitors.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monitor = monitors[index];
                var canonicalUrl = canonicalImportUrls[index];
                var matchingIds = await FindLogicalConversationOwnerIdsAsync(
                    connection,
                    transaction,
                    canonicalUrl,
                    maxCount: 2,
                    cancellationToken);

                if (matchingIds.Count > 1)
                    throw new InvalidOperationException($"Cannot import monitor '{monitor.Url}' because more than one local monitor owns that logical conversation identity.");

                await using var monitorCommand = connection.CreateCommand();
                monitorCommand.Transaction = transaction;

                if (matchingIds.Count == 1)
                {
                    monitorCommand.CommandText = """
                        UPDATE SavedMonitors SET
                            Title=$title, AutoReply=$autoReply, ReplyDelaySeconds=$replyDelay, TimerSeconds=$timer, Enabled=$enabled,
                            ConversationRotationEnabled=$rotationEnabled, NewChatStartMessage=$message, NewChatDelaySeconds=$newChatDelay,
                            RotationCooldownSeconds=$cooldown, MaxConversationRotations=$maxRotations,
                            ModelRoutingEnabled=$modelRouting, PreferredModel=$preferredModel, FallbackModel=$fallbackModel, UpdatedAt=$updatedAt
                        WHERE Id=$id;
                        """;
                    monitorCommand.Parameters.AddWithValue("$id", matchingIds[0]);
                    monitorsUpdated++;
                }
                else
                {
                    monitorCommand.CommandText = """
                        INSERT INTO SavedMonitors(
                            TabId,Title,Url,AutoReply,ReplyDelaySeconds,TimerSeconds,Enabled,ConversationRotationEnabled,
                            NewChatStartMessage,NewChatDelaySeconds,RotationCooldownSeconds,MaxConversationRotations,RotationCount,
                            ModelRoutingEnabled,PreferredModel,FallbackModel,CreatedAt,UpdatedAt)
                        VALUES('', $title,$url,$autoReply,$replyDelay,$timer,$enabled,$rotationEnabled,$message,$newChatDelay,$cooldown,$maxRotations,0,$modelRouting,$preferredModel,$fallbackModel,$createdAt,$updatedAt);
                        """;
                    monitorCommand.Parameters.AddWithValue("$url", canonicalUrl);
                    monitorCommand.Parameters.AddWithValue("$createdAt", now);
                    monitorsInserted++;
                }

                monitorCommand.Parameters.AddWithValue("$title", monitor.Title ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$autoReply", monitor.AutoReply ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$replyDelay", Math.Clamp(monitor.ReplyDelaySeconds, 0, 300));
                monitorCommand.Parameters.AddWithValue("$timer", Math.Clamp(monitor.TimerSeconds, 1, 60));
                monitorCommand.Parameters.AddWithValue("$enabled", monitor.Enabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$rotationEnabled", monitor.ConversationRotationEnabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$message", monitor.NewChatStartMessage ?? string.Empty);
                monitorCommand.Parameters.AddWithValue("$newChatDelay", Math.Clamp(monitor.NewChatDelaySeconds, 0, 600));
                monitorCommand.Parameters.AddWithValue("$cooldown", Math.Clamp(monitor.RotationCooldownSeconds, 0, 3600));
                monitorCommand.Parameters.AddWithValue("$maxRotations", Math.Clamp(monitor.MaxConversationRotations, 0, 1000));
                monitorCommand.Parameters.AddWithValue("$modelRouting", monitor.ModelRoutingEnabled ? 1 : 0);
                monitorCommand.Parameters.AddWithValue("$preferredModel", string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel);
                monitorCommand.Parameters.AddWithValue("$fallbackModel", string.IsNullOrWhiteSpace(monitor.FallbackModel) ? "Auto" : monitor.FallbackModel);
                monitorCommand.Parameters.AddWithValue("$updatedAt", now);
                await monitorCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return new ConfigurationImportDatabaseResult(settingsApplied, monitorsUpdated, monitorsInserted);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task<List<long>> FindLogicalConversationOwnerIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetUrl,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>(Math.Max(1, Math.Min(maxCount, 8)));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Url FROM SavedMonitors ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!ChatGptConversationIdentity.IsSame(reader.GetString(1), targetUrl))
                continue;
            ids.Add(reader.GetInt64(0));
            if (ids.Count >= maxCount)
                break;
        }
        return ids;
    }

    public async Task DeleteMonitorAsync(long monitorId, CancellationToken cancellationToken = default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="DELETE FROM SavedMonitors WHERE Id=$id;"; command.Parameters.AddWithValue("$id",monitorId); await command.ExecuteNonQueryAsync(cancellationToken); }

    public async Task AddConversationRotationAsync(long monitorId,string oldTabId,string newTabId,string trigger,string startMessage,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO ConversationRotations(MonitorId,OldTabId,NewTabId,Trigger,StartMessage,Timestamp) VALUES($m,$o,$n,$t,$s,$ts);"; command.Parameters.AddWithValue("$m",monitorId); command.Parameters.AddWithValue("$o",oldTabId??""); command.Parameters.AddWithValue("$n",newTabId??""); command.Parameters.AddWithValue("$t",trigger??""); command.Parameters.AddWithValue("$s",startMessage??""); command.Parameters.AddWithValue("$ts",DateTime.UtcNow.ToString("O")); await command.ExecuteNonQueryAsync(cancellationToken); }

    public async Task<List<MessageLog>> GetRecentLogsForMonitorAsync(long monitorId,int limit=12,CancellationToken cancellationToken=default)
    { var result=new List<MessageLog>(); await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="SELECT Id,Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status FROM MessageLogs WHERE MonitorId=$m ORDER BY Id DESC LIMIT $limit;"; command.Parameters.AddWithValue("$m",monitorId); command.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,50)); await using var reader=await command.ExecuteReaderAsync(cancellationToken); while(await reader.ReadAsync(cancellationToken)) result.Add(new MessageLog{Id=reader.GetInt64(0),Timestamp=ParseLocal(reader.GetString(1)),MonitorId=reader.IsDBNull(2)?null:reader.GetInt64(2),TabId=reader.GetString(3),TabTitle=reader.GetString(4),Direction=reader.GetString(5),Prompt=reader.GetString(6),Response=reader.GetString(7),Status=reader.GetString(8)}); result.Reverse(); return result; }

    public async Task SetSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count == 0) return;

        foreach (var pair in settings)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Settings batch cannot contain an empty key.", nameof(settings));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            foreach (var pair in settings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;";
                command.Parameters.AddWithValue("$key", pair.Key);
                command.Parameters.AddWithValue("$value", pair.Value ?? string.Empty);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task SetSettingAsync(string key,string value,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO AppSettings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;"; command.Parameters.AddWithValue("$key",key); command.Parameters.AddWithValue("$value",value??""); await command.ExecuteNonQueryAsync(cancellationToken); }
    public async Task<string?> GetSettingAsync(string key,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="SELECT Value FROM AppSettings WHERE Key=$key LIMIT 1;"; command.Parameters.AddWithValue("$key",key); return (await command.ExecuteScalarAsync(cancellationToken))?.ToString(); }
    public async Task<int> GetIntSettingAsync(string key,int defaultValue,int min,int max,CancellationToken cancellationToken=default)
    { var raw=await GetSettingAsync(key,cancellationToken); return int.TryParse(raw,out var value)?Math.Clamp(value,min,max):Math.Clamp(defaultValue,min,max); }
    public async Task AddLogAsync(string direction,string prompt,string response,string status,long? monitorId=null,string? tabId=null,string? tabTitle=null,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="INSERT INTO MessageLogs(Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status) VALUES($ts,$m,$id,$title,$dir,$p,$r,$s);"; command.Parameters.AddWithValue("$ts",DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$m",monitorId.HasValue?monitorId.Value:DBNull.Value); command.Parameters.AddWithValue("$id",tabId??""); command.Parameters.AddWithValue("$title",tabTitle??""); command.Parameters.AddWithValue("$dir",direction); command.Parameters.AddWithValue("$p",prompt??""); command.Parameters.AddWithValue("$r",response??""); command.Parameters.AddWithValue("$s",status??""); await command.ExecuteNonQueryAsync(cancellationToken); }
    public async Task<List<MessageLog>> GetRecentLogsAsync(int limit=500,CancellationToken cancellationToken=default)
    { var result=new List<MessageLog>(); await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="SELECT Id,Timestamp,MonitorId,TabId,TabTitle,Direction,Prompt,Response,Status FROM MessageLogs ORDER BY Id DESC LIMIT $limit;"; command.Parameters.AddWithValue("$limit",limit); await using var reader=await command.ExecuteReaderAsync(cancellationToken); while(await reader.ReadAsync(cancellationToken)) result.Add(new MessageLog{Id=reader.GetInt64(0),Timestamp=ParseLocal(reader.GetString(1)),MonitorId=reader.IsDBNull(2)?null:reader.GetInt64(2),TabId=reader.GetString(3),TabTitle=reader.GetString(4),Direction=reader.GetString(5),Prompt=reader.GetString(6),Response=reader.GetString(7),Status=reader.GetString(8)}); return result; }
    public async Task DeleteLogAsync(long id,CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="DELETE FROM MessageLogs WHERE Id=$id;"; command.Parameters.AddWithValue("$id",id); await command.ExecuteNonQueryAsync(cancellationToken); }
    public async Task ClearLogsAsync(CancellationToken cancellationToken=default)
    { await using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command=connection.CreateCommand(); command.CommandText="DELETE FROM MessageLogs;"; await command.ExecuteNonQueryAsync(cancellationToken); }
    private static DateTime ParseLocal(string value)=>DateTime.TryParse(value,out var dt)?dt.ToLocalTime():DateTime.MinValue;
}