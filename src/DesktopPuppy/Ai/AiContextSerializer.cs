using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopPuppy.Ai;

public static class AiContextSerializer
{
    private static readonly HashSet<string> CoreFacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "totalMinutes", "ApplicationSwitchCount", "longestContinuousMinutes", "deepNightMinutes",
        "categoryMinutes", "Interactions", "AiSuccessCount", "AiFailureCount", "AiRetryCount", "AiDisplayedCount", "recentComparison"
    };

    public static string Serialize(AiScene scene)
    {
        var application = Limit(scene.Application, 128);
        var title = Limit(scene.Title, 256);
        var detail = Limit(scene.SceneDetail, 512);
        var timeBand = Limit(scene.TimeBand, 64);
        var requiredStyle = Limit(scene.RequiredStyle, 64);
        var facts = ReadFacts(scene.FactsJson);
        var recentTitles = (scene.RecentTitles ?? []).Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Limit(t, 160)).Distinct().Take(6).ToList();
        var recent = scene.Recent.Take(12).Select(h => new { text = Limit(h.Candidate.Text, 500), style = Limit(h.Candidate.Style, 64) }).ToList();
        string Build() => JsonSerializer.Serialize(new
        {
            scene = scene.Kind.ToString(), application, title, timeBand,
            facts = facts.ToJsonString(), detail, requiredStyle, recentTitles, recent
        });

        var json = Build();
        // History and optional detail give way to the current scene's structured evidence.
        while (json.Length > 8000 && recent.Count > 0) { recent.RemoveAt(recent.Count - 1); json = Build(); }
        while (json.Length > 8000 && recentTitles.Count > 1) { recentTitles.RemoveAt(recentTitles.Count - 1); json = Build(); }
        foreach (var key in facts.Select(p => p.Key).Where(k => !CoreFacts.Contains(k) && k != "applications").Reverse().ToArray())
        {
            if (json.Length <= 8000) break;
            facts.Remove(key); json = Build();
        }
        if (facts["applications"] is JsonArray applications)
            while (json.Length > 8000 && applications.Count > 1) { applications.RemoveAt(applications.Count - 1); json = Build(); }
        while (json.Length > 8000 && detail.Length > 0) { detail = Limit(detail, detail.Length / 2); json = Build(); }
        while (json.Length > 8000 && title.Length > 0) { title = Limit(title, title.Length / 2); json = Build(); }
        while (json.Length > 8000 && recentTitles.Count > 0) { recentTitles.RemoveAt(recentTitles.Count - 1); json = Build(); }
        while (json.Length > 8000 && application.Length > 0) { application = Limit(application, application.Length / 2); json = Build(); }
        while (json.Length > 8000 && timeBand.Length > 0) { timeBand = Limit(timeBand, timeBand.Length / 2); json = Build(); }
        while (json.Length > 8000 && requiredStyle.Length > 0) { requiredStyle = Limit(requiredStyle, requiredStyle.Length / 2); json = Build(); }
        return json;
    }

    private static JsonObject ReadFacts(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject source) return [];
            var facts = new JsonObject();
            foreach (var property in source.OrderByDescending(p => CoreFacts.Contains(p.Key) ? 2 : p.Key == "applications" ? 1 : 0).Take(24))
            {
                if (property.Key.Equals("categoryMinutes", StringComparison.OrdinalIgnoreCase) || property.Key.Equals("Interactions", StringComparison.OrdinalIgnoreCase))
                {
                    var map = new JsonObject();
                    if (property.Value is JsonObject values)
                        foreach (var entry in values.Take(16))
                            if (Number(entry.Value) is { } number) map[Limit(entry.Key, 24)] = number;
                    facts[property.Key] = map;
                }
                else if (property.Key.Equals("recentComparison", StringComparison.OrdinalIgnoreCase))
                {
                    var comparison = new JsonObject();
                    if (property.Value is JsonObject values)
                    {
                        if (values["date"] is JsonValue date && date.TryGetValue<string>(out var dateText)) comparison["date"] = Limit(dateText, 10);
                        comparison["totalMinutes"] = Number(values["totalMinutes"]);
                        comparison["differenceMinutes"] = Number(values["differenceMinutes"]);
                    }
                    facts[property.Key] = comparison;
                }
                else if (CoreFacts.Contains(property.Key)) facts[property.Key] = Number(property.Value);
                else if (property.Key == "applications" && property.Value is JsonArray applications)
                {
                    var bounded = new JsonArray();
                    foreach (var application in applications.OfType<JsonObject>().Take(8))
                    {
                        var item = new JsonObject { ["minutes"] = Number(application["minutes"]) };
                        if (application["process"] is JsonValue process && process.TryGetValue<string>(out var processName)) item["process"] = Limit(processName, 64);
                        bounded.Add(item);
                    }
                    facts[property.Key] = bounded;
                }
                else facts[Limit(property.Key, 64)] = Compact(property.Value, 0);
            }
            return facts;
        }
        catch (JsonException) { return []; }
    }

    private static JsonNode? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number)
        ? JsonValue.Create(number) : null;

    private static JsonNode? Compact(JsonNode? node, int depth)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return JsonValue.Create(Limit(text, 64));
            return value.DeepClone();
        }
        if (depth >= 3) return null;
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var property in obj.Take(16)) result[Limit(property.Key, 32)] = Compact(property.Value, depth + 1);
            return result;
        }
        if (node is JsonArray array) return new JsonArray(array.Take(8).Select(n => Compact(n, depth + 1)).ToArray());
        return null;
    }

    private static string Limit(string text, int count)
    {
        if (text.Length <= count) return text;
        if (count > 0 && char.IsHighSurrogate(text[count - 1])) count--;
        return text[..count];
    }
}
