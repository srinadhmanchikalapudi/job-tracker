using System.Diagnostics;
using Microsoft.Win32;

namespace JobTracker.Core;

/// <summary>Where Windows keeps the person's Documents and Desktop (they may be redirected into OneDrive). A seam so tests need no real PC.</summary>
public interface IKnownFolders
{
    string Documents { get; }
    string Desktop { get; }
}

public sealed class WindowsKnownFolders : IKnownFolders
{
    public string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}

/// <param name="Path">The folder OneDrive syncs, for example C:\Users\Someone\OneDrive.</param>
/// <param name="Name">What to call it for the person: "OneDrive", or "OneDrive (work or school)".</param>
public sealed record OneDriveFolder(string Path, string Name);

/// <summary>
/// Finds the folders OneDrive syncs on this PC, and says whether a path is inside one. "Inside OneDrive" is what makes a copy survive a damaged
/// profile or a Windows reset: OneDrive keeps it online, and signing in again restores it.
/// </summary>
public sealed class OneDriveLocator(Func<IReadOnlyList<OneDriveFolder>>? folders = null, Func<bool>? isRunning = null)
{
    private readonly Func<IReadOnlyList<OneDriveFolder>> _folders = folders ?? Detect;
    private readonly Func<bool> _isRunning = isRunning ?? DefaultIsRunning;

    public IReadOnlyList<OneDriveFolder> Folders() => _folders();

    /// <summary>OneDrive is only syncing while its program is running.</summary>
    public bool IsRunning() => _isRunning();

    /// <summary>The OneDrive folder that contains <paramref name="path"/> (or is it), or null when the path is not synced.</summary>
    public OneDriveFolder? Containing(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string full;
        try
        {
            full = Normalise(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return Folders()
            .Where(f => full.Equals(Normalise(f.Path), StringComparison.OrdinalIgnoreCase) ||
                        full.StartsWith(Normalise(f.Path) + Separator, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Path.Length)
            .FirstOrDefault();
    }

    public bool IsInOneDrive(string path) => Containing(path) is not null;

    private const string Separator = "\\";

    private static string Normalise(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static bool DefaultIsRunning()
    {
        try
        {
            return Process.GetProcessesByName("OneDrive").Length > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static IReadOnlyList<OneDriveFolder> Detect()
    {
        var found = new List<OneDriveFolder>();

        void Add(string? path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            var full = Normalise(path);
            if (!found.Any(f => Normalise(f.Path).Equals(full, StringComparison.OrdinalIgnoreCase)))
                found.Add(new OneDriveFolder(full, name));
        }

        // Windows sets these for the signed-in OneDrive accounts.
        Add(Environment.GetEnvironmentVariable("OneDriveConsumer"), "OneDrive");
        Add(Environment.GetEnvironmentVariable("OneDriveCommercial"), "OneDrive (work or school)");
        Add(Environment.GetEnvironmentVariable("OneDrive"), "OneDrive");

        if (OperatingSystem.IsWindows())
            AddFromRegistry(Add);

        return found;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddFromRegistry(Action<string?, string> add)
    {
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            if (accounts is null)
                return;
            foreach (var name in accounts.GetSubKeyNames())
            {
                using var account = accounts.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is not string folder)
                    continue;
                var business = account.GetValue("Business") is int b && b == 1;
                add(folder, business ? "OneDrive (work or school)" : "OneDrive");
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // an unreadable registry key just means no extra account is found
        }
    }
}

public static class DataLocations
{
    public const string FolderName = DataRootAdvisor.SubfolderName;

    /// <summary>
    /// Where applications should go when the person has not chosen. A folder that already holds applications wins (so nothing is ever moved
    /// behind anyone's back, and a reinstall after a Windows reset finds the applications again once OneDrive has restored them). Otherwise
    /// Documents when it is synced by OneDrive, and the Desktop when it is not (the original default).
    /// </summary>
    public static string Recommended(IKnownFolders known, OneDriveLocator oneDrive)
    {
        var documents = Path.Combine(known.Documents, FolderName);
        var desktop = Path.Combine(known.Desktop, FolderName);

        if (DataRootAdvisor.LooksLikeDataFolder(documents))
            return documents;
        if (DataRootAdvisor.LooksLikeDataFolder(desktop))
            return desktop;
        return oneDrive.IsInOneDrive(known.Documents) ? documents : desktop;
    }

    /// <summary>True when Job Tracker would find the folder again by itself after a reinstall (it is where the default looks).</summary>
    public static bool IsWhereTheDefaultLooks(string dataRoot, IKnownFolders known)
    {
        var full = Path.GetFullPath(dataRoot).TrimEnd('\\', '/');
        return new[] { known.Documents, known.Desktop }
            .Select(f => Path.GetFullPath(Path.Combine(f, FolderName)).TrimEnd('\\', '/'))
            .Any(p => p.Equals(full, StringComparison.OrdinalIgnoreCase));
    }
}

public enum BackupKind
{
    /// <summary>The folder is inside OneDrive and OneDrive is running.</summary>
    InOneDrive,

    /// <summary>The folder is inside OneDrive but OneDrive is not running, so changes are not being uploaded.</summary>
    OneDriveNotRunning,

    /// <summary>OneDrive is on this PC but the applications are not in it.</summary>
    OnThisPcOnly,

    /// <summary>No OneDrive was found.</summary>
    NoOneDrive,
}

/// <param name="Headline">One short line.</param>
/// <param name="Detail">What it means for the person, in plain words.</param>
/// <param name="SuggestedFolder">Where to move the applications to get them into OneDrive, or null when no move would help.</param>
public sealed record BackupStatus(BackupKind Kind, string Headline, string Detail, string? SuggestedFolder);

public static class BackupAdvisor
{
    public static BackupStatus Evaluate(string dataRoot, IKnownFolders known, OneDriveLocator oneDrive)
    {
        if (oneDrive.Containing(dataRoot) is { } inside)
        {
            if (!oneDrive.IsRunning())
                return new BackupStatus(BackupKind.OneDriveNotRunning, "In OneDrive, but OneDrive is not running",
                    "Start OneDrive so your latest changes are uploaded.", null);

            var recover = DataLocations.IsWhereTheDefaultLooks(dataRoot, known)
                ? "Job Tracker finds them again by itself."
                : "Then choose this folder again in Settings.";
            return new BackupStatus(BackupKind.InOneDrive, "Backed up by OneDrive",
                $"Your applications are in {inside.Name}. If your profile is damaged or Windows is reset, sign in to OneDrive again and they come back. {recover}", null);
        }

        var folders = oneDrive.Folders();
        if (folders.Count == 0)
            return new BackupStatus(BackupKind.NoOneDrive, "Only on this PC",
                "OneDrive was not found, so nothing backs your applications up. Sign in to OneDrive, or choose a folder that another backup covers.", null);

        var suggested = oneDrive.IsInOneDrive(known.Documents)
            ? Path.Combine(known.Documents, DataLocations.FolderName)
            : Path.Combine(folders[0].Path, DataLocations.FolderName);
        return new BackupStatus(BackupKind.OnThisPcOnly, "Only on this PC",
            "If your profile is damaged or Windows is reset, these would be lost. Moving them into OneDrive keeps a copy online.", suggested);
    }
}
