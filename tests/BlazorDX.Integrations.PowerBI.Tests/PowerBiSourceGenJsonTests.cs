using System.Text;
using System.Text.Json;
using BlazorDX.Integrations.PowerBI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// Pins the zero-reflection JSON policy (ADR 0002): the wire types are reachable
/// only through the source-generated <see cref="PowerBiJsonContext"/>, and that is
/// what the service actually uses to parse the documented Power BI responses.
/// </summary>
public sealed class PowerBiSourceGenJsonTests
{
    [Fact]
    public void SourceGenContext_ExposesTypeInfoForEveryWireType()
    {
        // If these were not source-generated, Default.* would be null / unavailable.
        Assert.NotNull(PowerBiJsonContext.Default.PowerBiReportResponse);
        Assert.NotNull(PowerBiJsonContext.Default.GenerateTokenResponse);
        Assert.NotNull(PowerBiJsonContext.Default.GenerateTokenRequest);

        // The metadata reports the CLR type the generator emitted for it.
        Assert.Equal(typeof(GenerateTokenRequest), PowerBiJsonContext.Default.GenerateTokenRequest.Type);
    }

    [Fact]
    public void GenerateTokenRequest_SerializesThroughContext_ToDocumentedShape()
    {
        var json = JsonSerializer.Serialize(
            new GenerateTokenRequest(), PowerBiJsonContext.Default.GenerateTokenRequest);

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("View", parsed.RootElement.GetProperty("accessLevel").GetString());
    }

    [Fact]
    public async Task GenerateTokenResponse_ParsesThroughContext_CaseInsensitively()
    {
        // Mixed casing on the wire still maps, via the context's case-insensitive
        // option — without reflection.
        var payload = "{\"Token\":\"abc\",\"TokenId\":\"t1\",\"Expiration\":\"2030-05-01T10:00:00Z\"}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var parsed = await JsonSerializer.DeserializeAsync(
            stream, PowerBiJsonContext.Default.GenerateTokenResponse);

        Assert.NotNull(parsed);
        Assert.Equal("abc", parsed!.Token);
        Assert.Equal("t1", parsed.TokenId);
        Assert.Equal(new DateTimeOffset(2030, 5, 1, 10, 0, 0, TimeSpan.Zero), parsed.Expiration);
    }
}

/// <summary>
/// Covers the DI surface and the trivial token provider directly: options
/// validation and the static provider's guards.
/// </summary>
public sealed class PowerBiRegistrationTests
{
    [Fact]
    public void AddBlazorDXPowerBi_MissingWorkspace_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddBlazorDXPowerBi(o => o.ApiBaseUrl = "https://api.powerbi.com"));
    }

    [Fact]
    public void AddBlazorDXPowerBi_NonAbsoluteApiUrl_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddBlazorDXPowerBi(o =>
            {
                o.ApiBaseUrl = "not-a-url";
                o.WorkspaceId = "ws";
            }));
    }

    [Fact]
    public void AddBlazorDXPowerBi_RegistersScopedEmbedService()
    {
        var services = new ServiceCollection();
        services.AddBlazorDXPowerBi(o =>
            {
                o.ApiBaseUrl = "https://api.powerbi.com";
                o.WorkspaceId = "ws";
            })
            .UseTokenProvider(new StaticPowerBiTokenProvider("t"));

        var descriptor = Assert.Single(
            services, d => d.ServiceType == typeof(IPowerBiEmbedService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void StaticTokenProvider_BlankToken_Throws() =>
        Assert.Throws<ArgumentException>(() => new StaticPowerBiTokenProvider("  "));

    [Fact]
    public async Task StaticTokenProvider_ReturnsConfiguredToken()
    {
        var provider = new StaticPowerBiTokenProvider("the-token");
        Assert.Equal("the-token", await provider.GetAadTokenAsync());
    }
}
