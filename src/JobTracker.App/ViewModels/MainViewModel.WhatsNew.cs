using System.Diagnostics;
using JobTracker.Core;
using JobTracker.Core.Updates;

namespace JobTracker.App.ViewModels;

/// <summary>One block in the What's new window.</summary>
/// <param name="Badge">A small label such as "Update available" or "You have this version"; empty for none.</param>
public sealed record WhatsNewEntry(string Title, string Badge, IReadOnlyList<string> Items);

/// <summary>The part of the main view model that shows what changed in each version, inside the app.</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// What changed, newest first: the update that is waiting (from its GitHub release), then the versions that ship inside the program.
    /// Works offline: the notes of the installed versions are part of the program.
    /// </summary>
    public IReadOnlyList<WhatsNewEntry> BuildWhatsNew()
    {
        var entries = new List<WhatsNewEntry>();
        var current = AppInfo.CurrentVersion;
        Version? pendingVersion = null;

        if (_updates?.Available is { } update && ReleaseNotes.FromGitHubBody(update.Notes, update.Version) is { } pending)
        {
            entries.Add(new WhatsNewEntry(pending.Title, "Update available", pending.Items));
            pendingVersion = update.Version;
        }

        foreach (var section in ReleaseNotes.History())
        {
            if (pendingVersion is not null && UpdateVersions.Display(section.Version) == UpdateVersions.Display(pendingVersion))
                continue; // the one waiting is already shown, from the release itself
            var badge = UpdateVersions.Display(section.Version) == UpdateVersions.Display(current) ? "You have this version" : "";
            entries.Add(new WhatsNewEntry(section.Title, badge, section.Items));
        }

        return entries;
    }

    /// <summary>
    /// Called once when the app starts. Returns what to show if the person has just updated (the versions since the last one they saw), or null.
    /// A brand-new install shows nothing. The version is remembered, so each update is announced once.
    /// </summary>
    public IReadOnlyList<WhatsNewEntry>? WhatsNewOnStartup()
    {
        // The development switches that skip the update check also skip this, so test runs and debugging never use it up.
        if (Debugger.IsAttached || Environment.GetEnvironmentVariable(UpdateService.NoCheckEnvVar) == "1")
            return null;

        var current = AppInfo.CurrentVersion;
        var seen = UpdateVersions.Parse(_settings.LastSeenVersion);
        var action = WhatsNewPolicy.Decide(_settings.LastSeenVersion, _settings.IsFirstRun, current);
        if (action == WhatsNewAction.Nothing)
            return null;

        _settings.LastSeenVersion = UpdateVersions.Display(current);
        Try(() => _settings.Save());
        if (action == WhatsNewAction.RecordOnly)
            return null;

        var entries = WhatsNewPolicy.SectionsToShow(ReleaseNotes.History(), seen, current)
            .Select(s => new WhatsNewEntry(s.Title, UpdateVersions.Display(s.Version) == UpdateVersions.Display(current) ? "You have this version" : "", s.Items))
            .ToList();
        return entries.Count == 0 ? null : entries;
    }
}