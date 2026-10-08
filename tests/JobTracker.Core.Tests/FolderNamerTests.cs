namespace JobTracker.Core.Tests;

public class FolderNamerTests
{
    [Theory]
    [InlineData("Acme Corp", "Acme Corp")]
    [InlineData("  Acme   Corp  ", "Acme Corp")]
    [InlineData("AT&T: Mobility/Wireless?", "AT&T MobilityWireless")]
    [InlineData("Acme Inc.", "Acme Inc")]
    [InlineData("...", "Untitled")]
    [InlineData("", "Untitled")]
    [InlineData("CON", "CON_")]
    [InlineData("con.txt", "con.txt_")]
    public void Sanitize_ProducesSafeSegment(string input, string expected) =>
        Assert.Equal(expected, FolderNamer.Sanitize(input));

    [Fact]
    public void Sanitize_TruncatesLongNames() =>
        Assert.True(FolderNamer.Sanitize(new string('a', 300)).Length <= 80);

    [Fact]
    public void ApplicationFolderName_IsDatePrefixed() =>
        Assert.Equal("2026-10-07_Senior Backend Engineer",
            FolderNamer.ApplicationFolderName(new DateOnly(2026, 10, 7), "Senior Backend Engineer"));

    [Fact]
    public void MakeUnique_AppendsCounterWhenTaken()
    {
        using var temp = new TempDir();
        var first = Path.Combine(temp.Path, "2026-10-07_Dev");
        Assert.Equal(first, FolderNamer.MakeUnique(first));

        Directory.CreateDirectory(first);
        Assert.Equal(first + " (2)", FolderNamer.MakeUnique(first));

        Directory.CreateDirectory(first + " (2)");
        Assert.Equal(first + " (3)", FolderNamer.MakeUnique(first));
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jobtracker-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
