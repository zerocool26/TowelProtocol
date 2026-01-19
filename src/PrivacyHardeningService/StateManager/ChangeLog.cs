using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningContracts.Responses;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Persistent change log using SQLite for rollback and auditing
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ChangeLog : IDisposable
{
    private const int LatestSchemaVersion = 1;

    private readonly ILogger<ChangeLog> _logger;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public ChangeLog(ILogger<ChangeLog> logger)
    {
        _logger = logger;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dbDirectory = Path.Combine(appDataPath, "PrivacyHardeningFramework");
        Directory.CreateDirectory(dbDirectory);

        _databasePath = Path.Combine(dbDirectory, "changelog.db");
        InitializeDatabaseAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var createTablesCommand = connection.CreateCommand();
            createTablesCommand.CommandText = @"
                CREATE TABLE IF NOT EXISTS changes (
                    change_id TEXT PRIMARY KEY,
                    operation TEXT,
                    policy_id TEXT NOT NULL,
                    applied_at TEXT NOT NULL,
                    mechanism TEXT NOT NULL,
                    description TEXT,
                    previous_state TEXT,
                    new_state TEXT,
                    success INTEGER NOT NULL,
                    error_message TEXT,
                    snapshot_id TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_policy_id ON changes(policy_id);
                CREATE INDEX IF NOT EXISTS idx_applied_at ON changes(applied_at);
                CREATE INDEX IF NOT EXISTS idx_snapshot_id ON changes(snapshot_id);

                CREATE TABLE IF NOT EXISTS snapshots (
                    snapshot_id TEXT PRIMARY KEY,
                    created_at TEXT NOT NULL,
                    description TEXT,
                    os_version TEXT,
                    os_build TEXT,
                    os_sku TEXT,
                    computer_name TEXT,
                    domain_joined INTEGER,
                    mdm_managed INTEGER,
                    defender_tamper_protection INTEGER,
                    restore_point_id TEXT
                );

                CREATE TABLE IF NOT EXISTS snapshot_policies (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    snapshot_id TEXT NOT NULL,
                    policy_id TEXT NOT NULL,
                    is_applied INTEGER NOT NULL,
                    current_value TEXT,
                    FOREIGN KEY (snapshot_id) REFERENCES snapshots(snapshot_id)
                );

                CREATE INDEX IF NOT EXISTS idx_snapshot_policies ON snapshot_policies(snapshot_id);
            ";

            await createTablesCommand.ExecuteNonQueryAsync(cancellationToken);

            await EnsureSchemaVersioningAsync(connection, cancellationToken);
            await ApplyMigrationsAsync(connection, cancellationToken);
            _logger.LogInformation("Database initialized at {Path}", _databasePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static async Task EnsureSchemaVersioningAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS schema_versions (
                version INTEGER NOT NULL,
                applied_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_schema_versions_version ON schema_versions(version);
        ";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetCurrentSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result == null || result == DBNull.Value)
        {
            return 0;
        }

        return Convert.ToInt32(result);
    }

    private static async Task RecordSchemaVersionAsync(SqliteConnection connection, int version, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO schema_versions(version, applied_at) VALUES (@v, @t);";
        cmd.Parameters.AddWithValue("@v", version);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var current = await GetCurrentSchemaVersionAsync(connection, cancellationToken);

        if (current > LatestSchemaVersion)
        {
            // Fail-closed: we don't understand a newer DB schema.
            throw new InvalidOperationException(
                $"Database schema version {current} is newer than supported version {LatestSchemaVersion}." +
                " Please upgrade the service binary.");
        }

        for (var v = current + 1; v <= LatestSchemaVersion; v++)
        {
            _logger.LogInformation("Applying database migration v{Version}...", v);

            switch (v)
            {
                case 1:
                    // Migration v1: add columns introduced after the initial schema.
                    await EnsureColumnExistsAsync(connection, "changes", "operation", "TEXT", cancellationToken);
                    await EnsureColumnExistsAsync(connection, "snapshots", "os_sku", "TEXT", cancellationToken);
                    await EnsureColumnExistsAsync(connection, "snapshots", "mdm_managed", "INTEGER", cancellationToken);
                    await EnsureColumnExistsAsync(connection, "snapshots", "defender_tamper_protection", "INTEGER", cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Missing migration implementation for schema version {v}.");
            }

            await RecordSchemaVersionAsync(connection, v, cancellationToken);
            _logger.LogInformation("Database migration v{Version} applied.", v);
        }
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnType,
        CancellationToken cancellationToken)
    {
        // sqlite doesn't support ALTER TABLE ADD COLUMN IF NOT EXISTS, so check schema first.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
            if (reader.FieldCount >= 2 && string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(ChangeRecord[] changes, CancellationToken cancellationToken)
    {
        if (changes.Length == 0) return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            foreach (var change in changes)
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO changes
                    (change_id, operation, policy_id, applied_at, mechanism, description,
                     previous_state, new_state, success, error_message, snapshot_id)
                    VALUES
                    (@changeId, @operation, @policyId, @appliedAt, @mechanism, @description,
                     @previousState, @newState, @success, @errorMessage, @snapshotId)
                ";

                command.Parameters.AddWithValue("@changeId", change.ChangeId);
                command.Parameters.AddWithValue("@operation", change.Operation.ToString());
                command.Parameters.AddWithValue("@policyId", change.PolicyId);
                command.Parameters.AddWithValue("@appliedAt", change.AppliedAt.ToString("O"));
                command.Parameters.AddWithValue("@mechanism", change.Mechanism.ToString());
                command.Parameters.AddWithValue("@description", change.Description ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@previousState", change.PreviousState ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@newState", change.NewState ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@success", change.Success ? 1 : 0);
                command.Parameters.AddWithValue("@errorMessage", change.ErrorMessage ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@snapshotId", change.SnapshotId ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Saved {Count} change records to database", changes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save changes to database");
            throw;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<ChangeRecord[]> GetChangesForPolicyAsync(string policyId, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT change_id, policy_id, applied_at, mechanism, description,
                       previous_state, new_state, success, error_message, snapshot_id, operation
                FROM changes
                WHERE policy_id = @policyId
                ORDER BY applied_at DESC
            ";
            command.Parameters.AddWithValue("@policyId", policyId);

            var changes = new List<ChangeRecord>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                changes.Add(new ChangeRecord
                {
                    ChangeId = reader.GetString(0),
                    Operation = ParseOperation(reader, 10),
                    PolicyId = reader.GetString(1),
                    AppliedAt = DateTime.Parse(reader.GetString(2)),
                    Mechanism = Enum.Parse<MechanismType>(reader.GetString(3)),
                    Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    PreviousState = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewState = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Success = reader.GetInt32(7) == 1,
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                    SnapshotId = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return changes.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve changes for policy {PolicyId}", policyId);
            return Array.Empty<ChangeRecord>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<ChangeRecord[]> GetAllChangesAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT change_id, policy_id, applied_at, mechanism, description,
                       previous_state, new_state, success, error_message, snapshot_id, operation
                FROM changes
                ORDER BY applied_at DESC
            ";

            var changes = new List<ChangeRecord>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                changes.Add(new ChangeRecord
                {
                    ChangeId = reader.GetString(0),
                    Operation = ParseOperation(reader, 10),
                    PolicyId = reader.GetString(1),
                    AppliedAt = DateTime.Parse(reader.GetString(2)),
                    Mechanism = Enum.Parse<MechanismType>(reader.GetString(3)),
                    Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    PreviousState = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewState = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Success = reader.GetInt32(7) == 1,
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                    SnapshotId = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return changes.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all changes");
            return Array.Empty<ChangeRecord>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<string> CreateSnapshotAsync(string? description, SystemInfo systemInfo, string? restorePointId, CancellationToken cancellationToken)
    {
        var snapshotId = Guid.NewGuid().ToString();

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO snapshots
                (snapshot_id, created_at, description, os_version, os_build, os_sku, computer_name, domain_joined, mdm_managed, defender_tamper_protection, restore_point_id)
                VALUES
                (@snapshotId, @createdAt, @description, @osVersion, @osBuild, @osSku, @computerName, @domainJoined, @mdmManaged, @defenderTamper, @restorePointId)
            ";

            command.Parameters.AddWithValue("@snapshotId", snapshotId);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("@description", description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@osVersion", systemInfo.WindowsVersion ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@osBuild", systemInfo.WindowsBuild.ToString());
            command.Parameters.AddWithValue("@osSku", systemInfo.WindowsSku ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@computerName", Environment.MachineName);
            command.Parameters.AddWithValue("@domainJoined", systemInfo.IsDomainJoined ? 1 : 0);
            command.Parameters.AddWithValue("@mdmManaged", systemInfo.IsMDMManaged ? 1 : 0);
            command.Parameters.AddWithValue("@defenderTamper", systemInfo.DefenderTamperProtectionEnabled ? 1 : 0);
            command.Parameters.AddWithValue("@restorePointId", restorePointId ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Created snapshot {SnapshotId}", snapshotId);
            return snapshotId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot");
            throw;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task SaveSnapshotPoliciesAsync(string snapshotId, SnapshotPolicyState[] policies, CancellationToken cancellationToken)
    {
        if (policies.Length == 0) return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            // Avoid duplicates if called multiple times for the same snapshot id.
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM snapshot_policies WHERE snapshot_id = @snapshotId";
                delete.Parameters.AddWithValue("@snapshotId", snapshotId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var policy in policies)
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO snapshot_policies
                    (snapshot_id, policy_id, is_applied, current_value)
                    VALUES
                    (@snapshotId, @policyId, @isApplied, @currentValue)
                ";

                command.Parameters.AddWithValue("@snapshotId", snapshotId);
                command.Parameters.AddWithValue("@policyId", policy.PolicyId);
                command.Parameters.AddWithValue("@isApplied", policy.IsApplied ? 1 : 0);
                command.Parameters.AddWithValue("@currentValue", policy.CurrentValue ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save snapshot policies for snapshot {SnapshotId}", snapshotId);
            throw;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<SystemSnapshot?> GetLatestSnapshotAsync(bool includeHistory, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var latestCmd = connection.CreateCommand();
            latestCmd.CommandText = @"
                SELECT snapshot_id, created_at, description, os_build, os_sku, restore_point_id
                FROM snapshots
                ORDER BY created_at DESC
                LIMIT 1
            ";

            using var reader = await latestCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var snapshotId = reader.GetString(0);
            var createdAt = DateTime.Parse(reader.GetString(1));
            var description = reader.IsDBNull(2) ? null : reader.GetString(2);

            var windowsBuild = 0;
            if (!reader.IsDBNull(3))
            {
                var raw = reader.GetString(3);
                _ = int.TryParse(raw, out windowsBuild);
            }

            var windowsSku = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4);
            var restorePointId = reader.IsDBNull(5) ? null : reader.GetString(5);

            var appliedPolicies = await GetAppliedPoliciesForSnapshotAsync(connection, snapshotId, cancellationToken);
            var changeHistory = includeHistory
                ? await GetChangesForSnapshotAsync(connection, snapshotId, cancellationToken)
                : Array.Empty<ChangeRecord>();

            return new SystemSnapshot
            {
                SnapshotId = snapshotId,
                CreatedAt = createdAt,
                WindowsBuild = windowsBuild,
                WindowsSku = windowsSku,
                AppliedPolicies = appliedPolicies,
                ChangeHistory = changeHistory,
                RestorePointId = restorePointId,
                Description = description
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load latest snapshot");
            return null;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static async Task<string[]> GetAppliedPoliciesForSnapshotAsync(SqliteConnection connection, string snapshotId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT policy_id
            FROM snapshot_policies
            WHERE snapshot_id = @snapshotId AND is_applied = 1
            ORDER BY policy_id
        ";
        command.Parameters.AddWithValue("@snapshotId", snapshotId);

        var applied = new List<string>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied.ToArray();
    }

    private async Task<ChangeRecord[]> GetChangesForSnapshotAsync(SqliteConnection connection, string snapshotId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT change_id, policy_id, applied_at, mechanism, description,
                   previous_state, new_state, success, error_message, snapshot_id, operation
            FROM changes
            WHERE snapshot_id = @snapshotId
            ORDER BY applied_at DESC
        ";
        command.Parameters.AddWithValue("@snapshotId", snapshotId);

        var changes = new List<ChangeRecord>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new ChangeRecord
            {
                ChangeId = reader.GetString(0),
                Operation = ParseOperation(reader, 10),
                PolicyId = reader.GetString(1),
                AppliedAt = DateTime.Parse(reader.GetString(2)),
                Mechanism = Enum.Parse<MechanismType>(reader.GetString(3)),
                Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                PreviousState = reader.IsDBNull(5) ? null : reader.GetString(5),
                NewState = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Success = reader.GetInt32(7) == 1,
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                SnapshotId = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return changes.ToArray();
    }

    public async Task<ChangeRecord[]> GetChangesBySnapshotIdAsync(string snapshotId, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            // Re-use private helper logic but with new connection
            return await GetChangesForSnapshotAsync(connection, snapshotId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get changes for snapshot {SnapshotId}", snapshotId);
            return Array.Empty<ChangeRecord>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<SnapshotPolicyState[]> GetSnapshotPolicyStatesAsync(string snapshotId, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT policy_id, is_applied, current_value
                FROM snapshot_policies
                WHERE snapshot_id = @snapshotId
            ";
            command.Parameters.AddWithValue("@snapshotId", snapshotId);

            var states = new List<SnapshotPolicyState>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                states.Add(new SnapshotPolicyState
                {
                    PolicyId = reader.GetString(0),
                    IsApplied = reader.GetInt32(1) == 1,
                    CurrentValue = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }

            return states.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load policy states for snapshot {SnapshotId}", snapshotId);
            return Array.Empty<SnapshotPolicyState>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public void Dispose()
    {
        _dbLock?.Dispose();
    }

    private static ChangeOperation ParseOperation(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || ordinal >= reader.FieldCount)
        {
            return ChangeOperation.Unknown;
        }

        if (reader.IsDBNull(ordinal))
        {
            return InferOperation(reader);
        }

        var raw = reader.GetString(ordinal);
        if (Enum.TryParse<ChangeOperation>(raw, ignoreCase: true, out var op))
        {
            return op;
        }

        return InferOperation(reader);
    }

    private static ChangeOperation InferOperation(SqliteDataReader reader)
    {
        // Try to infer from the description (legacy DB rows before operation column existed).
        var descOrdinal = 4;
        var description = reader.FieldCount > descOrdinal && !reader.IsDBNull(descOrdinal) ? reader.GetString(descOrdinal) : string.Empty;

        if (description.StartsWith("Reverted", StringComparison.OrdinalIgnoreCase))
        {
            return ChangeOperation.Revert;
        }

        if (description.StartsWith("Removed firewall", StringComparison.OrdinalIgnoreCase))
        {
            return ChangeOperation.Revert;
        }

        return ChangeOperation.Apply;
    }
}
