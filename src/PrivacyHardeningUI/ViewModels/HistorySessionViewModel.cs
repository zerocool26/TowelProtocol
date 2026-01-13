using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PrivacyHardeningContracts.Models;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class HistorySessionViewModel : ObservableObject
{
    public string? SnapshotId { get; }
    public DateTime AppliedAt { get; }
    
    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<ChangeRecord> Changes { get; } = new();

    public HistorySessionViewModel(string? snapshotId, IEnumerable<ChangeRecord> changes)
    {
        SnapshotId = snapshotId;
        var first = changes.FirstOrDefault();
        AppliedAt = first?.AppliedAt ?? DateTime.MinValue;
        
        foreach (var c in changes.OrderByDescending(c => c.AppliedAt))
        {
            Changes.Add(c);
        }
    }

    public string SessionTitle => string.IsNullOrWhiteSpace(SnapshotId) 
        ? $"Unassociated Changes ({AppliedAt:yyyy-MM-dd HH:mm})"
        : $"Session {SnapshotId[..Math.Min(SnapshotId.Length, 8)]} ({AppliedAt:yyyy-MM-dd HH:mm})";

    public int TotalChanges => Changes.Count;
    public int SuccessCount => Changes.Count(c => c.Success);
    public int FailureCount => Changes.Count(c => !c.Success);
}
