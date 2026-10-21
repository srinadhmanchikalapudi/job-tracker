using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace JobTracker.Core.Ai;

/// <summary>
/// The models an AI service offers, for the drop-down in Settings. The list comes from the service itself, so it is never out of date, with a short
/// built-in list for when the service cannot be reached. Only the model names are read.
/// </summary>
public static class ModelCatalog
{
    /// <summary>Models that are known to exist, shown until (or if) the real list can be fetched.</summary>
    public static IReadOnlyList<string> Fallback(string provider) => provider == AiProviders.Anthropic
        ? ["claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5", "claude-fable-5-1"]
        : ["anthropic/claude-sonnet-5.5", "anthropic/claude-opus-5.5", "anthropic/claude-haiku-5.5"];

    /// <summary>Whether the service will list its models without a key (OpenRouter does, Anthropic does not).</summary>
    public static bool NeedsKey(string provider) => provider == AiProviders.Anthropic;

    /// <summary>
    /// Fetches the model names. OpenRouter's list is public, so no key is sent to it; Anthropic's needs the key in the usual header.
    /// Claude models come first in both.
    /// </summary>
    /// <exception cref="ModelException">The service could not be reached or refused (a bad key, for example).</exception>
    public static async Task<IReadOnlyList<string>> ListAsync(string provider, string? apiKey, HttpClient http, CancellationToken cancellationToken = default)
    {
        var ids = provider == AiProviders.Anthropic
            ? await ListAnthropicAsync(apiKey, http, cancellationToken).ConfigureAwait(false)
            : await ListOpenRouterAsync(http, cancellationToken).ConfigureAwait(false);
        return Order(ids);
    }

    private static async Task<List<string>> ListOpenRouterAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var answer = await ChatHttp.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        return Ids(answer["data"] as JsonArray);
    }

    private static async Task<List<string>> ListAnthropicAsync(string? apiKey, HttpClient http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ModelException("Save an Anthropic key first. Anthropic only lists its models for a key.");

        var ids = new List<string>();
        string? after = null;
        for (var page = 0; page < 10; page++) // a safety limit: nobody has a thousand models
        {
            var url = "https://api.anthropic.com/v1/models?limit=100" + (after is null ? "" : "&after_id=" + Uri.EscapeDataString(after));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            var answer = await ChatHttp.SendAsync(http, request, cancellationToken).ConfigureAwait(false);

            ids.AddRange(Ids(answer["data"] as JsonArray));
            if (answer["has_more"]?.GetValue<bool>() != true || answer["last_id"]?.GetValue<string>() is not { Length: > 0 } last)
                break;
            after = last;
        }

        return ids;
    }

    // ":batch" entries are the same model run as a slow background job, which these chat requests cannot use, so they are not offered.
    private static List<string> Ids(JsonArray? data) =>
        (data ?? []).Select(item => item?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id!.EndsWith(":batch", StringComparison.OrdinalIgnoreCase))
            .Select(id => id!).Distinct().ToList();

    /// <summary>Claude models first (they are what the prompts are written for), in the order the service gave them (newest first); then the rest alphabetically.</summary>
    internal static IReadOnlyList<string> Order(IEnumerable<string> ids)
    {
        var all = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool IsClaude(string id) => id.Contains("claude", StringComparison.OrdinalIgnoreCase);
        var claude = all.Where(IsClaude);
        var others = all.Where(id => !IsClaude(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        return claude.Concat(others).ToList();
    }
}
