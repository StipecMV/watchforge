using Microsoft.AspNetCore.Cors.Infrastructure;

namespace WatchForge.Api;

/// <summary>
/// CORS policy provider (S5-1) — stavia policy LAZY pri každom requeste z live
/// IConfiguration (sekcia WatchForge:Api). Na rozdiel od AddPolicy(name, Action),
/// ktorý sa evaluuje eager počas registrácie, tak policy vidí finálnu konfiguráciu —
/// WebApplicationFactory môže v integračných testoch override CORS originy cez
/// ConfigureAppConfiguration. IConfiguration sa číta priamo (nie cez IOptions,
/// pretože ValidateOnStart by options instanciu vytvoril a zacachoval už pri štarte).
/// </summary>
public sealed class ApiCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly IConfiguration _configuration;

    public ApiCorsPolicyProvider(IConfiguration configuration)
        => _configuration = configuration;

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var api = _configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()
                  ?? new ApiOptions();

        var builder = new CorsPolicyBuilder();
        builder.WithOrigins(api.CorsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
        if (api.CorsAllowCredentials)
            builder.AllowCredentials();

        return Task.FromResult<CorsPolicy?>(builder.Build());
    }
}
