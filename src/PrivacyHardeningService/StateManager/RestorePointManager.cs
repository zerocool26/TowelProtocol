using System.Management.Automation;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Manages Windows System Restore points via PowerShell
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestorePointManager
{
    private readonly ILogger<RestorePointManager> _logger;

    public RestorePointManager(ILogger<RestorePointManager> logger)
    {
        _logger = logger;
    }

    public async Task<string?> CreateRestorePointAsync(string description, CancellationToken cancellationToken)
    {
        try
        {
            // First check if System Restore is enabled
            if (!await IsSystemRestoreEnabledAsync(cancellationToken))
            {
                _logger.LogWarning("System Restore is not enabled on this system");
                return null;
            }

            _logger.LogInformation("Creating system restore point: {Description}", description);

            using var ps = PowerShell.Create();

            // Use Checkpoint-Computer cmdlet to create restore point
            ps.AddCommand("Checkpoint-Computer")
              .AddParameter("Description", description)
              .AddParameter("RestorePointType", "MODIFY_SETTINGS");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            if (ps.HadErrors)
            {
                var errors = string.Join(", ", ps.Streams.Error.Select(e => e.ToString()));
                _logger.LogError("Failed to create restore point: {Errors}", errors);
                return null;
            }

            // Get the most recent restore point to retrieve the sequence number
            var restorePointId = await GetLatestRestorePointIdAsync(cancellationToken);

            if (restorePointId != null)
            {
                _logger.LogInformation("System restore point created successfully: {RestorePointId}", restorePointId);
                return restorePointId;
            }
            else
            {
                _logger.LogWarning("Restore point created but unable to retrieve ID");
                return "created";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create system restore point");
            return null;
        }
    }

    private async Task<bool> IsSystemRestoreEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var ps = PowerShell.Create();

            // Check if System Restore is enabled on any drive
            ps.AddCommand("Get-ComputerRestorePoint")
              .AddParameter("ErrorAction", "SilentlyContinue");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            // If we can query restore points, System Restore is likely enabled
            // Even if results are empty, the cmdlet working means SR is available
            return !ps.HadErrors;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetLatestRestorePointIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var ps = PowerShell.Create();

            ps.AddCommand("Get-ComputerRestorePoint")
              .AddParameter("ErrorAction", "SilentlyContinue");

            ps.AddCommand("Select-Object")
              .AddParameter("Last", 1);

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            if (results.Count > 0)
            {
                var restorePoint = results[0];
                var sequenceNumber = restorePoint.Properties["SequenceNumber"]?.Value;

                if (sequenceNumber != null)
                {
                    return sequenceNumber.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve latest restore point ID");
        }

        return null;
    }

    public async Task<bool> RestorePointExistsAsync(string restorePointId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(restorePointId))
            return false;

        try
        {
            using var ps = PowerShell.Create();

            ps.AddCommand("Get-ComputerRestorePoint")
              .AddParameter("ErrorAction", "SilentlyContinue");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            foreach (var result in results)
            {
                var sequenceNumber = result.Properties["SequenceNumber"]?.Value?.ToString();
                if (sequenceNumber == restorePointId)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check restore point existence");
        }

        return false;
    }

    public async Task<RestorePointInfo[]> GetRestorePointsAsync(CancellationToken cancellationToken)
    {
        var restorePoints = new List<RestorePointInfo>();

        try
        {
            using var ps = PowerShell.Create();

            ps.AddCommand("Get-ComputerRestorePoint")
              .AddParameter("ErrorAction", "SilentlyContinue");

            var results = await Task.Run(() => ps.Invoke(), cancellationToken);

            foreach (var result in results)
            {
                var sequenceNumber = result.Properties["SequenceNumber"]?.Value?.ToString();
                var description = result.Properties["Description"]?.Value?.ToString();
                var creationTime = result.Properties["CreationTime"]?.Value;

                if (sequenceNumber != null && description != null)
                {
                    restorePoints.Add(new RestorePointInfo
                    {
                        SequenceNumber = sequenceNumber,
                        Description = description,
                        CreationTime = creationTime is DateTime dt ? dt : DateTime.MinValue
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate restore points");
        }

        return restorePoints.ToArray();
    }
}

public sealed class RestorePointInfo
{
    public required string SequenceNumber { get; init; }
    public required string Description { get; init; }
    public DateTime CreationTime { get; init; }
}
