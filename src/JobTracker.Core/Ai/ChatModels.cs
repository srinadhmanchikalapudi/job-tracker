using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobTracker.Core.Ai;

/// <summary>A problem talking to the AI provider, worded for the person (never contains the key).</summary>
public sealed class ModelException(string message) : Exception(message);

/// <summary>One question, one answer. The rest of the app does not know or care which provider is behind it.</summary>
public interface IChatModel
{
    string Provider { get; }
    Task<string> CompleteAsync(string system, string user, CancellationToken cancellationToken = default);
}

public static class AiProviders
{
    public const string Anthropic = "Anthropic";
    public const string OpenRouter = "OpenRouter";

    public static IReadOnlyList<string> All { get; } = [OpenRouter, Anthropic];

    public static string DefaultModel(string provider) => provider switch
    {
        Anthropic => "claude-sonnet-5-5",
        _ => "anthropic/claude-sonnet-5.5",
    };

    /// <summary>The name the key is saved under in Windows Credential Manager.</summary>
    public static string SecretName(string provider) => $"api-key-{provider.ToLowerInvariant()}";

    public static string Normalize(string? provider) =>
        All.FirstOrDefault(p => string.Equals(p, provider?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? OpenRouter;

    /// <summary>Where the text goes, in the words shown to the person before the first use.</summary>
    public static string Destination(string provider) => provider == Anthropic ? "Anthropic (api.anthropic.com)" : "OpenRouter (openrouter.ai), which passes it on to the model's maker";
}

internal static class ChatHttp
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<JsonNode> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ModelException("The AI service did not answer in time. Try again in a moment.");
        }
        catch (HttpRequestException)
        {
            throw new ModelException("Could not reach the AI service. Check your internet connection and try again.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ModelException(Describe(response.StatusCode, body));

            try
            {
                return JsonNode.Parse(body) ?? throw new ModelException("The AI service sent an empty answer.");
            }
            catch (JsonException)
            {
                throw new ModelException("The AI service sent an answer this app could not read.");
            }
        }
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        var detail = ProviderMessage(body);
        var suffix = detail.Length > 0 ? $" The service said: {detail}" : "";
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The AI service did not accept your key. Check it in Settings." + suffix,
            HttpStatusCode.PaymentRequired => "The AI service says the account has no credit left." + suffix,
            HttpStatusCode.NotFound => "The AI service does not know that model. Check the model name in Settings." + suffix,
            HttpStatusCode.TooManyRequests => "The AI service is limiting requests right now. Wait a minute and try again." + suffix,
            >= HttpStatusCode.InternalServerError => "The AI service had a problem on its side. Try again in a moment." + suffix,
            _ => $"The AI service refused the request ({(int)status})." + suffix,
        };
    }

    private static string ProviderMessage(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            var message = error is JsonValue v && v.TryGetValue<string>(out var s) ? s : error?["message"]?.GetValue<string>();
            return message is { Length: > 0 } ? (message.Length > 200 ? message[..200] + "…" : message) : "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "";
        }
    }
}

/// <summary>Anthropic's Messages API.</summary>
public sealed class AnthropicChatModel(HttpClient http, string apiKey, string model) : IChatModel
{
    public string Provider => AiProviders.Anthropic;

    public async Task<string> CompleteAsync(string system, string user, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 3000,
            ["temperature"] = 0.3,
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var answer = await ChatHttp.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        var text = string.Concat((answer["content"] as JsonArray ?? []).Select(block => block?["text"]?.GetValue<string>()));
        return text.Length > 0 ? text : throw new ModelException("The AI service sent an empty answer.");
    }
}

/// <summary>OpenRouter's OpenAI-style chat endpoint, which fronts many models with one key.</summary>
public sealed class OpenRouterChatModel(HttpClient http, string apiKey, string model) : IChatModel
{
    public string Provider => AiProviders.OpenRouter;

    public async Task<string> CompleteAsync(string system, string user, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 3000,
            ["temperature"] = 0.3,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("X-Title", "Job Tracker");

        var answer = await ChatHttp.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        var text = answer["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return string.IsNullOrEmpty(text) ? throw new ModelException("The AI service sent an empty answer.") : text;
    }
}

/// <summary>Builds the model the person set up, or says in plain words what is missing.</summary>
public static class ChatModelFactory
{
    public static IChatModel? TryCreate(AppSettings settings, ISecretStore secrets, HttpClient http, out string problem)
    {
        var provider = AiProviders.Normalize(settings.AiProvider);
        var key = secrets.Get(AiProviders.SecretName(provider));
        if (string.IsNullOrWhiteSpace(key))
        {
            problem = $"No {provider} key is saved yet. Add one in Settings, under AI assistance.";
            return null;
        }

        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? AiProviders.DefaultModel(provider) : settings.AiModel.Trim();
        problem = "";
        return provider == AiProviders.Anthropic
            ? new AnthropicChatModel(http, key, model)
            : new OpenRouterChatModel(http, key, model);
    }
}
