using System.Net;
using System.Text.Json;

namespace App.Tests.Infrastructure;

public static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>GETs the URL, asserts 200 and returns the JSON body.</summary>
    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    /// <summary>Asserts the status, and for a problem response returns its type.</summary>
    public static async Task<string?> AssertStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        if (body.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    /// <summary>Asserts a problem response with the given status and type.</summary>
    public static async Task AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode expected, string type)
    {
        Assert.Equal(type, await response.AssertStatusAsync(expected));
    }
}
