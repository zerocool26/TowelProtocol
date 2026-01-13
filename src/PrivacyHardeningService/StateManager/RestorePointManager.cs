using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace PrivacyHardeningService.StateManager;

/// <summary>
/// Manages Windows System Restore points via WMI (root\default:SystemRestore)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestorePointManager
{
    private readonly ILogger<RestorePointManager> _logger;
    private const uint RestorePointTypeModifySettings = 12;
    private const uint EventTypeBeginSystemChange = 100;

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

            cancellationToken.ThrowIfCancellationRequested();

            using var systemRestoreClass = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var inParams = systemRestoreClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = RestorePointTypeModifySettings;
            inParams["EventType"] = EventTypeBeginSystemChange;

            using var outParams = systemRestoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
            var returnValue = (uint?)outParams?["ReturnValue"] ?? 1u;

            if (returnValue != 0u)
            {
                _logger.LogError("Failed to create restore point (WMI). ReturnValue={ReturnValue}", returnValue);
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
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(@"\\.\root\default", "SELECT SequenceNumber FROM SystemRestore");
            using var results = searcher.Get();
            _ = results.Count; // Force evaluation to catch COM/WMI failures.
            return true;
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
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(@"\\.\root\default", "SELECT SequenceNumber FROM SystemRestore");
            using var results = searcher.Get();

            uint max = 0;
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var value = obj["SequenceNumber"];
                    if (value == null) continue;

                    if (uint.TryParse(value.ToString(), out var seq) && seq > max)
                    {
                        max = seq;
                    }
                }
            }

            return max > 0 ? max.ToString() : null;
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
            cancellationToken.ThrowIfCancellationRequested();

            if (!uint.TryParse(restorePointId, out var seq))
            {
                return false;
            }

            using var searcher = new ManagementObjectSearcher(@"\\.\root\default", $"SELECT SequenceNumber FROM SystemRestore WHERE SequenceNumber = {seq}");
            using var results = searcher.Get();
            return results.Count > 0;
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
            cancellationToken.ThrowIfCancellationRequested();

            using var searcher = new ManagementObjectSearcher(@"\\.\root\default", "SELECT SequenceNumber, Description, CreationTime FROM SystemRestore");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var sequenceNumber = obj["SequenceNumber"]?.ToString();
                    var description = obj["Description"]?.ToString();
                    var creationTimeRaw = obj["CreationTime"]?.ToString();

                    if (string.IsNullOrWhiteSpace(sequenceNumber) || string.IsNullOrWhiteSpace(description))
                    {
                        continue;
                    }

                    var creationTime = DateTime.MinValue;
                    if (!string.IsNullOrWhiteSpace(creationTimeRaw))
                    {
                        try
                        {
                            creationTime = ManagementDateTimeConverter.ToDateTime(creationTimeRaw);
                        }
                        catch
                        {
                            creationTime = DateTime.MinValue;
                        }
                    }

                    restorePoints.Add(new RestorePointInfo
                    {
                        SequenceNumber = sequenceNumber,
                        Description = description,
                        CreationTime = creationTime
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate restore points");
        }

        return restorePoints
            .OrderByDescending(r => r.CreationTime)
            .ToArray();
    }
}

public sealed class RestorePointInfo
{
    public required string SequenceNumber { get; init; }
    public required string Description { get; init; }
    public DateTime CreationTime { get; init; }
}
