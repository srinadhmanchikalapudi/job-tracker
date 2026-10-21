using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobTracker.Core.Ai;

/// <summary>
/// Reads the JSON a model was asked to reply with, however it chose to dress it: inside a code fence, after a sentence, as an object or a bare list, under
/// a slightly different name. If there is no JSON at all (the model explained instead of answering), the error says what it said, so the person is not
/// left with "could not read".
/// </summary>
internal static class ModelJson
{
    /// <summary>The first object or list in the reply that is valid JSON.</summary>
    /// <exception cref="ModelException">There is none; the message quotes the start of what the model wrote.</exception>
    public static JsonNode Find(string reply)
    {
        for (var i = 0; i < reply.Length; i++)
        {
            if (reply[i] is not ('{' or '['))
                continue;
            var end = MatchingClose(reply, i);
            if (end < 0)
                continue;
            try
            {
                if (JsonNode.Parse(reply[i..(end + 1)]) is { } node and (JsonObject or JsonArray))
                    return node;
            }
            catch (JsonException)
            {
                // not JSON after all (a brace in a sentence); keep looking
            }
        }

        throw new ModelException(Snippet(reply) is { Length: > 0 } said
            ? $"The AI answered in words instead of bullets. It said: “{said}”"
            : "The AI sent an empty answer. Try again.");
    }

    /// <summary>The start of what the model wrote, on one line, for showing to the person.</summary>
    public static string Snippet(string reply, int max = 320)
    {
        var text = string.Join(' ', reply.Replace("```json", "").Replace("```", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > max ? text[..max].TrimEnd() + "…" : text;
    }

    /// <summary>The items of the list: the root itself if it is a list, otherwise the list under the first of these names that exists.</summary>
    public static IEnumerable<JsonNode> Items(JsonNode root, params string[] names)
    {
        if (root is JsonArray array)
            return array.Where(n => n is not null).Select(n => n!);
        foreach (var name in names)
            if (root[name] is JsonArray list)
                return list.Where(n => n is not null).Select(n => n!);
        return [];
    }

    /// <summary>The words of an item: the item itself if it is a string, or its text under one of the usual names.</summary>
    public static string? TextOf(JsonNode item)
    {
        if (item is JsonValue value && value.TryGetValue<string>(out var plain))
            return plain.Trim();
        foreach (var name in new[] { "text", "bullet", "content", "suggestion", "rewrite" })
            if ((item as JsonObject)?[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                return s.Trim();
        return null;
    }

    public static string? StringOf(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
            if ((node as JsonObject)?[name] is JsonValue v && v.TryGetValue<string>(out var s))
                return s.Trim();
        return null;
    }

    /// <summary>A member of an item that is an object; null for anything else (an item may be a bare string).</summary>
    public static JsonNode? Member(JsonNode item, string name) => (item as JsonObject)?[name];

    // The index of the bracket that closes the one at 'open', skipping brackets inside strings; -1 if it never closes.
    private static int MatchingClose(string text, int open)
    {
        var stack = new Stack<char>();
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': stack.Push('}'); break;
                case '[': stack.Push(']'); break;
                case '}' or ']':
                    if (stack.Count == 0 || stack.Pop() != c)
                        return -1;
                    if (stack.Count == 0)
                        return i;
                    break;
            }
        }

        return -1;
    }
}