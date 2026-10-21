using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobTracker.Core.Updates;

/// <summary>
/// Looks at the repository's latest GitHub release (public, no key needed; the API's "latest" is never a draft or a pre-release) and says
/// whether it is newer than the running version. One small request, sent only when the user allows update checks or asks for one.
/// GitHub lets a program without an account ask about 60 times an hour. When that is used up, the check falls back to the release web page (which has
/// no such limit and says which version is newest), and no further request goes to the limited service until the time GitHub gave.
/// </summary>
public sealed partial class GitHubUpdateChecker(HttpClient http, string owner, string repo, Func<DateTimeOffset>? clock = null) : IUpdateChecker
{
    public const string InstallerPrefix = "JobTracker-Setup-";

    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset? _limitedUntil;

    [GeneratedRegex(@"/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TagInUrl();

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            // Still inside the time GitHub gave: do not ask the limited service again (it would only count against the person).
            if (_limitedUntil is { } until && _now() < until)
                return await FallbackAsync(current, until, ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return UpdateCheckResult.UpToDate; // nothing has been published yet
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                _limitedUntil = LimitEnds(response);
                return await FallbackAsync(current, _limitedUntil, ct);
            }
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed($"GitHub answered with an error ({(int)response.StatusCode}).");

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Read(doc.RootElement, current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed("GitHub did not answer in time. Check your internet connection.");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed("Could not reach GitHub to look for updates: " + ex.Message);
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed("GitHub's answer could not be read.");
        }
    }

    /// <summary>When GitHub says the limit lifts: Retry-After (seconds) or X-RateLimit-Reset (a Unix time), whichever it sent; null when it said nothing.</summary>
    private DateTimeOffset? LimitEnds(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } wait)
            return _now() + wait;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        return null;
    }

    /// <summary>
    /// The newest release read from its web page instead of the limited service: the page address after redirects ends in /releases/tag/vX.Y.Z. That gives the
    /// version and the page to download from, but not the setup program's checksum, so an update found this way is downloaded from the page, not installed
    /// from inside the app.
    /// </summary>
    private async Task<UpdateCheckResult> FallbackAsync(Version current, DateTimeOffset? limitedUntil, CancellationToken ct)
    {
        try
        {
            using var page = await http.GetAsync($"https://github.com/{owner}/{repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
            var final = page.RequestMessage?.RequestUri;
            if (page.IsSuccessStatusCode && final is not null && TagInUrl().Match(final.AbsoluteUri) is { Success: true } m)
            {
                var tag = Uri.UnescapeDataString(m.Groups["tag"].Value);
                if (UpdateVersions.Parse(tag) is not { } version || !UpdateVersions.IsNewer(version, current))
                    return UpdateCheckResult.UpToDate;
                return UpdateCheckResult.Available(new UpdateInfo(version, tag, final.AbsoluteUri, null, null));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // fall through to the message: the limit is the thing to explain
        }

        var when = limitedUntil is { } at ? $" It will allow it again at {at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}." : " Try again in a little while.";
        return UpdateCheckResult.Failed("GitHub is limiting requests from this connection right now (it allows about 60 an hour without an account)." + when);
    }

    private UpdateCheckResult Read(JsonElement release, Version current)
    {
        var tag = Text(release, "tag_name");
        if (UpdateVersions.Parse(tag) is not { } version)
            return UpdateCheckResult.UpToDate; // a tag that is not a version is not an update
        if (!UpdateVersions.IsNewer(version, current))
            return UpdateCheckResult.UpToDate;

        string? url = null, sha = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name") ?? "";
                if (!name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                url = Text(asset, "browser_download_url");
                var digest = Text(asset, "digest");
                if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest["sha256:".Length..].Trim().ToLowerInvariant();
                break;
            }
        }

        var page = Text(release, "html_url") ?? $"https://github.com/{owner}/{repo}/releases/latest";
        return UpdateCheckResult.Available(new UpdateInfo(version, tag!, page, url, sha, Text(release, "body")));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
