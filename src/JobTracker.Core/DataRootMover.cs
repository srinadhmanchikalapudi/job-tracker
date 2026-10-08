namespace JobTracker.Core;

/// <summary>One application that was copied to the new folder.</summary>
/// <param name="OldRelative">Its path below the old data folder, for example "Acme Corp\2026-10-07_Backend".</param>
/// <param name="NewRelative">Its path below the new folder (the same unless a folder with that name was already there).</param>
public sealed record MovedApplication(string OldRelative, string NewRelative, string OldFullPath);

public sealed record MoveResult(IReadOnlyList<MovedApplication> Applications, int FilesCopied, long BytesCopied, int BulletsCopied);

public sealed record MoveProgress(int Done, int Total, string Current);

/// <summary>A message written for the user. The data folder is left exactly as it was.</summary>
public sealed class DataRootException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Moves the saved applications to another folder safely: everything is copied and checked first, the starred bullets are carried over,
/// and only after the caller has switched to the new folder are the originals deleted (<see cref="DeleteOriginals"/>). If anything goes
/// wrong before that, the copies are removed again and nothing has changed.
/// </summary>
public static class DataRootMover
{
    /// <summary>A sentence for the user if <paramref name="newRoot"/> cannot be used instead of <paramref name="oldRoot"/>; otherwise null.</summary>
    public static string? Validate(string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(newRoot))
            return "Choose a folder.";

        string oldFull, newFull;
        try
        {
            oldFull = Normalise(oldRoot);
            newFull = Normalise(newRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That is not a valid folder.";
        }

        if (Same(oldFull, newFull))
            return "That is the folder Job Tracker is already using.";
        if (IsInside(newFull, oldFull))
            return "The new folder is inside the current one. Choose a folder outside it.";
        if (IsInside(oldFull, newFull))
            return "The current folder is inside the new one. Choose a folder outside it, or a new empty folder.";
        return null;
    }

    /// <summary>
    /// Copies every saved application (and its starred bullets) from <paramref name="oldRoot"/> to <paramref name="newRoot"/> and checks the copy.
    /// A folder name that is already taken in the new place gets " (2)" and its bullets are pointed at the new name. The originals are not touched.
    /// </summary>
    /// <exception cref="DataRootException">Nothing was changed: anything copied so far has been removed again.</exception>
    public static MoveResult CopyAll(string oldRoot, string newRoot, IProgress<MoveProgress>? progress = null, CancellationToken ct = default)
    {
        if (Validate(oldRoot, newRoot) is { } problem)
            throw new DataRootException(problem);

        var apps = new ApplicationStore(oldRoot).List();
        var createdFolders = new List<string>();
        var createdCompanyFolders = new List<string>();
        var newRootExisted = Directory.Exists(newRoot);
        byte[]? bulletsBackup = null;
        var bulletsFile = BulletsFile(newRoot);
        var bulletsExisted = File.Exists(bulletsFile);
        var moved = new List<MovedApplication>();
        int files = 0, bulletsCopied = 0;
        long bytes = 0;

        try
        {
            Directory.CreateDirectory(newRoot);

            foreach (var app in apps)
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(oldRoot, app.FolderPath);
                progress?.Report(new MoveProgress(moved.Count, apps.Count, relative));

                var destination = FolderNamer.MakeUnique(Path.Combine(newRoot, relative));
                if (Path.GetDirectoryName(destination) is { } companyFolder && !Directory.Exists(companyFolder))
                    createdCompanyFolders.Add(companyFolder);
                createdFolders.Add(destination);
                var (copiedFiles, copiedBytes) = CopyDirectory(app.FolderPath, destination, ct);
                Verify(app.FolderPath, destination);

                files += copiedFiles;
                bytes += copiedBytes;
                moved.Add(new MovedApplication(relative, Path.GetRelativePath(newRoot, destination), app.FolderPath));
            }

            // The bullets go last: they are the only thing written into a place that may already hold the user's data.
            if (moved.Count > 0)
            {
                var folderMap = moved.ToDictionary(m => m.OldRelative, m => m.NewRelative, StringComparer.OrdinalIgnoreCase);
                var incoming = new BulletStore(oldRoot).List().Where(b => folderMap.ContainsKey(b.AppFolder)).ToList();
                if (incoming.Count > 0)
                {
                    if (bulletsExisted)
                        bulletsBackup = File.ReadAllBytes(bulletsFile);
                    bulletsCopied = new BulletStore(newRoot).MergeFrom(incoming, folderMap);
                }
            }

            progress?.Report(new MoveProgress(moved.Count, apps.Count, ""));
            return new MoveResult(moved, files, bytes, bulletsCopied);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or DataRootException)
        {
            Rollback(createdFolders, createdCompanyFolders, newRoot, newRootExisted, bulletsFile, bulletsExisted, bulletsBackup);
            if (ex is OperationCanceledException)
                throw;
            if (ex is DataRootException)
                throw;
            throw new DataRootException(
                "Your applications could not be copied, so nothing was changed. " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Removes the originals after the copy was checked and the new folder is in use. A file that cannot be deleted (open in another program)
    /// is reported, not treated as a failure: the applications are safely in the new folder.
    /// </summary>
    /// <returns>Sentences about anything that could not be removed; empty when everything was.</returns>
    public static IReadOnlyList<string> DeleteOriginals(string oldRoot, MoveResult result)
    {
        var warnings = new List<string>();
        var companies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in result.Applications)
        {
            try
            {
                ForceDelete(app.OldFullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not remove {app.OldFullPath}: {ex.Message}");
            }

            if (Path.GetDirectoryName(app.OldFullPath) is { } company)
                companies.Add(company);
        }

        foreach (var company in companies)
        {
            try
            {
                if (Directory.Exists(company) && !Directory.EnumerateFileSystemEntries(company).Any())
                    Directory.Delete(company);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not remove the empty folder {company}: {ex.Message}");
            }
        }

        try
        {
            var movedFolders = result.Applications.Select(a => a.OldRelative).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var oldBullets = new BulletStore(oldRoot);
            oldBullets.RemoveWhere(b => movedFolders.Contains(b.AppFolder));
            if (oldBullets.List().Count == 0)
            {
                var file = BulletsFile(oldRoot);
                if (File.Exists(file))
                    File.Delete(file);
                var dir = Path.GetDirectoryName(file)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not clean up the old starred bullets file: {ex.Message}");
        }

        return warnings;
    }

    private static string BulletsFile(string root) => Path.Combine(root, ApplicationStore.AppDataFolder, "bullets.json");

    private static (int Files, long Bytes) CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        int files = 0;
        long bytes = 0;

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: false);
            files++;
            bytes += new FileInfo(target).Length;
        }

        return (files, bytes);
    }

    /// <summary>The copy must hold the same files with the same sizes, or it is not trusted and the originals stay.</summary>
    private static void Verify(string source, string destination)
    {
        var expected = Snapshot(source);
        var actual = Snapshot(destination);
        if (expected.Count != actual.Count || expected.Any(kv => !actual.TryGetValue(kv.Key, out var length) || length != kv.Value))
            throw new DataRootException("The copy did not match the original, so nothing was changed. Your files are untouched.");
    }

    private static Dictionary<string, long> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);

    private static void Rollback(
        List<string> createdFolders, List<string> createdCompanyFolders, string newRoot, bool newRootExisted,
        string bulletsFile, bool bulletsExisted, byte[]? bulletsBackup)
    {
        // Best effort: a failure to clean up must not hide the original problem.
        foreach (var folder in createdFolders)
        {
            try { ForceDelete(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try
        {
            if (bulletsBackup is not null)
                File.WriteAllBytes(bulletsFile, bulletsBackup);
            else if (!bulletsExisted && File.Exists(bulletsFile))
                File.Delete(bulletsFile);

            var appData = Path.GetDirectoryName(bulletsFile)!;
            if (!bulletsExisted && Directory.Exists(appData) && !Directory.EnumerateFileSystemEntries(appData).Any())
                Directory.Delete(appData);

            foreach (var company in createdCompanyFolders)
            {
                if (Directory.Exists(company) && !Directory.EnumerateFileSystemEntries(company).Any())
                    Directory.Delete(company);
            }

            if (!newRootExisted && Directory.Exists(newRoot) && !Directory.EnumerateFileSystemEntries(newRoot).Any())
                Directory.Delete(newRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    /// <summary>Deletes a folder even if it holds read-only files (the attribute makes a plain delete fail).</summary>
    private static void ForceDelete(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
    }

    private static string Normalise(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string child, string parent) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Helps a user who picks a folder that already holds other things not to scatter company folders among them.</summary>
public static class DataRootAdvisor
{
    public const string SubfolderName = "JobApplications";

    /// <summary>
    /// True when <paramref name="path"/> is a folder with other things in it that is not already a Job Tracker folder, so the company folders
    /// would end up mixed in with them (for example the Desktop or Documents).
    /// </summary>
    public static bool ShouldSuggestSubfolder(string path)
    {
        if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
            return false;
        return !LooksLikeDataFolder(path);
    }

    /// <summary>A folder that Job Tracker already uses: it has the .jobtracker folder or at least one saved application.</summary>
    public static bool LooksLikeDataFolder(string path) =>
        Directory.Exists(Path.Combine(path, ApplicationStore.AppDataFolder)) || new ApplicationStore(path).List().Count > 0;

    public static string Suggested(string path) => Path.Combine(path, SubfolderName);
}
