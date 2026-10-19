using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>One tab above the applications: All, Full-time or Contract, with how many applications it holds.</summary>
public sealed partial class JobTypeTab(JobType? type, string name) : ObservableObject
{
    /// <summary>The type this tab shows, or null for the "All" tab.</summary>
    public JobType? Type { get; } = type;

    public string Name { get; } = name;

    [ObservableProperty] private int _count;
}

/// <summary>A choice in a job type drop-down.</summary>
public sealed record JobTypeOption(string Label, JobType Value);