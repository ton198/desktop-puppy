using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DesktopPuppy.Ai;
using Xunit;

namespace DesktopPuppy.Tests.Ai;

public sealed class AiHttpTests
{
    [Fact]
    public async Task DeepSeek_sends_json_mode_six_candidates_and_auth_only_in_header()
    {
        var key = Guid.NewGuid().ToString("N");
        var handler = new Handler(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(new { candidates = AiPipelineTests.Candidates.Select(c => new { text = c.Text, style = c.Style }) }) } } } }));
        var provider = new DeepSeekContentProvider(new HttpClient(handler), () => key);
        var result = await provider.GenerateAsync(new("editor", "test", "day", []), [], 2, CancellationToken.None);
        Assert.Equal(6, result.Count); Assert.Contains("deepseek-flash", handler.Body); Assert.Contains("json_object", handler.Body); Assert.DoesNotContain(key, handler.Body);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.Url); Assert.Equal(key, handler.Auth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{\\\"candidates\\\":[]}\"}}]}")]
    public async Task DeepSeek_rejects_empty_invalid_or_wrong_count(string response)
    {
        var provider = new DeepSeekContentProvider(new HttpClient(new Handler(response)), () => Guid.NewGuid().ToString());
        await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(new("", "", "day", []), [], 1, CancellationToken.None));
    }

    [Fact]
    public async Task Jev_uses_one_choice_and_maps_official_answer()
    {
        var handler = new Handler("{\"answers\":{\"selection\":{\"type\":\"choice\",\"choice\":\"a\",\"confidence\":0.8,\"probabilities\":{\"a\":0.8,\"b\":0.2}}}}");
        var provider = new JevDecisionProvider(new HttpClient(handler), () => Guid.NewGuid().ToString());
        var result = await provider.SelectAsync(new("editor", "test", "day", []), AiPipelineTests.Candidates.Take(2).ToArray(), CancellationToken.None);
        Assert.Equal("a", result.CandidateId); Assert.Equal(.8, result.Confidence);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", handler.Url);
        using var json = JsonDocument.Parse(handler.Body); Assert.Single(json.RootElement.GetProperty("questions").EnumerateObject()); Assert.Contains("jev-latest", handler.Body);
    }

    [Fact]
    public async Task Http_failure_does_not_leak_response_or_key()
    {
        var key = Guid.NewGuid().ToString(); var handler = new Handler(key) { Status = HttpStatusCode.Unauthorized };
        var error = await Assert.ThrowsAsync<AiProviderException>(() => new DeepSeekContentProvider(new HttpClient(handler), () => key).GenerateAsync(new("", "", "day", []), [], 1, CancellationToken.None));
        Assert.DoesNotContain(key, error.ToString()); Assert.Equal("Http", error.Kind);
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Body = "", Url = "", Auth = ""; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Url = request.RequestUri!.AbsoluteUri; Auth = request.Headers.Authorization!.Parameter!; Body = await request.Content!.ReadAsStringAsync(ct); return new(Status) { Content = new StringContent(response, Encoding.UTF8, "application/json") }; }
    }
}
