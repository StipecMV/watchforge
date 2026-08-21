using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WatchForge.Api.Tests;

/// <summary>
/// S5-1: scaffold WatchForge.Api — overuje, že MVC pipeline beží, CORS policy funguje
/// a error handling vracia RFC 9457 ProblemDetails (500 výnimka, 404 neznáma cesta,
/// 400 validačná chyba ApiController).
/// </summary>
public class ScaffoldTests
{
    private static WebApplicationFactory<WatchForge.Api.ApiEntryPoint> CreateFactory()
        => new WebApplicationFactory<WatchForge.Api.ApiEntryPoint>();

    // ── health / scaffold funguje ────────────────────────────────────────

    [Test]
    public async Task Health_ReturnsOkWithStatus()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!["status"]?.GetValue<string>()).IsEqualTo("ok");
        await Assert.That(body!["serverTimeUtc"]).IsNotNull();
    }

    // ── CORS ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Cors_AllowedOrigin_ReturnsAllowOriginHeader()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.Add("Origin", "http://localhost:4200");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").First())
            .IsEqualTo("http://localhost:4200");
    }

    [Test]
    public async Task Cors_DisallowedOrigin_NoAllowOriginHeader()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task Cors_Preflight_AllowedOrigin_Returns204WithPolicyHeaders()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/system/health");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Methods")).IsTrue();
    }

    [Test]
    public async Task Cors_AllowedOrigins_AreConfigurableViaEnvVar()
    {
        // Env vars sú súčasťou app konfigurácie (CreateBuilder ich pridáva pri štarte)
        // → fungujú aj pod WebApplicationFactory (na rozdiel od ConfigureAppConfiguration/
        // UseSetting, ktoré factory aplikuje do host configu, nie do app configu).
        // Konfiguračný kľúč: WatchForge__Api__CorsAllowedOrigins__N (property
        // ApiOptions.CorsAllowedOrigins — žiadny "Cors:" segment!).
        // Index 2 (nie 0) — neovplyvňuje ostatné testy bežiace paralelne.
        const string envKey = "WatchForge__Api__CorsAllowedOrigins__2";
        Environment.SetEnvironmentVariable(envKey, "http://env.example");
        WebApplicationFactory<WatchForge.Api.ApiEntryPoint> factory;
        try
        {
            factory = CreateFactory();
            _ = factory.Services; // build hostu kým je env var nastavená
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }

        await using (factory)
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
            request.Headers.Add("Origin", "http://env.example");

            var response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
            await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").First())
                .IsEqualTo("http://env.example");
        }
    }

    // ── error handling (RFC 9457 ProblemDetails) ─────────────────────────

    [Test]
    public async Task UnknownRoute_ReturnsProblemDetails404()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(body!["status"]?.GetValue<int>()).IsEqualTo(404);
        await Assert.That(body!["title"]).IsNotNull();
    }

    [Test]
    public async Task UnhandledException_ReturnsProblemDetails500()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/_scaffold/boom");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(body!["status"]?.GetValue<int>()).IsEqualTo(500);
        await Assert.That(body!["title"]).IsNotNull();
    }

    [Test]
    public async Task ModelValidationError_ReturnsProblemDetails400WithErrors()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // "value" chýba (null) → [Required] zlyhá → ApiController vráti 400 ProblemDetails
        var response = await client.PostAsJsonAsync("/api/v1/_scaffold/echo", new { });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        await Assert.That(body!["status"]?.GetValue<int>()).IsEqualTo(400);
        var errors = body!["errors"] as JsonObject;
        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.ContainsKey("Value")).IsTrue();
    }
}
