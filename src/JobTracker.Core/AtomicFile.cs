using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobTracker.Core;

internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes to a temp file then moves it into place, so a crash cannot leave a half-written file.</summary>
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }

    public static void WriteJson<T>(string path, T value) =>
        WriteAllText(path, JsonSerializer.Serialize(value, Json));

    public static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
