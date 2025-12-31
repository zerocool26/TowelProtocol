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
                    computer_name TEXT,
                    domain_joined INTEGER,
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
                    (change_id, policy_id, applied_at, mechanism, description,
                     previous_state, new_state, success, error_message, snapshot_id)
                    VALUES
                    (@changeId, @policyId, @appliedAt, @mechanism, @description,
                     @previousState, @newState, @success, @errorMessage, @snapshotId)
                ";

                command.Parameters.AddWithValue("@changeId", change.ChangeId);
                command.Parameters.AddWithValue("@policyId", change.PolicyId);
                command.Parameters.AddWithValue("@appliedAt", change.AppliedAt.ToString("O"));
                command.Parameters.AddWithValue("@mechanism", change.Mechanism.ToString());
                command.Parameters.AddWithValue("@description", change.Description ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@previousState", change.PreviousState ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@newState", change.NewState ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@success", change.Success ? 1 : 0);
                command.Parameters.AddWithValue("@errorMessage", change.ErrorMessage ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@snapshotId", (object)DBNull.Value);

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
                       previous_state, new_state, success, error_message
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
                    PolicyId = reader.GetString(1),
                    AppliedAt = DateTime.Parse(reader.GetString(2)),
                    Mechanism = Enum.Parse<MechanismType>(reader.GetString(3)),
                    Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    PreviousState = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewState = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Success = reader.GetInt32(7) == 1,
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8)
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
                       previous_state, new_state, success, error_message
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
                    PolicyId = reader.GetString(1),
                    AppliedAt = DateTime.Parse(reader.GetString(2)),
                    Mechanism = Enum.Parse<MechanismType>(reader.GetString(3)),
                    Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    PreviousState = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewState = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Success = reader.GetInt32(7) == 1,
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8)
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

    public async Task<string> CreateSnapshotAsync(string description, SystemInfo systemInfo, CancellationToken cancellationToken)
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
                (snapshot_id, created_at, description, os_version, os_build, computer_name, domain_joined, restore_point_id)
                VALUES
                (@snapshotId, @createdAt, @description, @osVersion, @osBuild, @computerName, @domainJoined, @restorePointId)
            ";

            command.Parameters.AddWithValue("@snapshotId", snapshotId);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("@description", description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@osVersion", systemInfo.WindowsVersion ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@osBuild", systemInfo.WindowsBuild.ToString());
            command.Parameters.AddWithValue("@computerName", Environment.MachineName);
            command.Parameters.AddWithValue("@domainJoined", systemInfo.IsDomainJoined ? 1 : 0);
            command.Parameters.AddWithValue("@restorePointId", (object)DBNull.Value);

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

    public void Dispose()
    {
        _dbLock?.Dispose();
    }
}
