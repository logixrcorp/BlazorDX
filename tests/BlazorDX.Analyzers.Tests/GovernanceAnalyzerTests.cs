using BlazorDX.Analyzers;
using Xunit;

namespace BlazorDX.Analyzers.Tests;

/// <summary>Proves the build-time governance rules actually fire.</summary>
public sealed class GovernanceAnalyzerTests
{
    [Fact]
    public async Task DX1000_fires_for_a_file_over_the_cap()
    {
        // 1001 lines of body inside a class comfortably exceeds the 1000-line cap.
        string body = string.Join("\n", Enumerable.Range(0, 1001).Select(i => $"    // filler {i}"));
        string source = $"class TooLong\n{{\n{body}\n}}";

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new FileLengthAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "DX1000");
    }

    [Fact]
    public async Task DX1000_silent_for_a_short_file()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync("class Small { }", new FileLengthAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "DX1000");
    }

    [Fact]
    public async Task DX1001_fires_for_MarkupString_from_runtime_data_but_not_constants()
    {
        const string source = """
            namespace Microsoft.AspNetCore.Components
            {
                public struct MarkupString { public MarkupString(string value) { } }
            }

            namespace Test
            {
                public class Renderer
                {
                    public object Dangerous(string input) =>
                        new Microsoft.AspNetCore.Components.MarkupString(input);

                    public object Safe() =>
                        new Microsoft.AspNetCore.Components.MarkupString("constant markup");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new RawHtmlAnalyzer());

        Assert.Single(diagnostics, d => d.Id == "DX1001");
    }

    [Fact]
    public async Task DX1002_fires_for_Singleton_state_registration()
    {
        const string source = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class Reg
                {
                    public static IServiceCollection AddSingleton<T>(this IServiceCollection services) => services;
                }
            }

            namespace Test
            {
                using Microsoft.Extensions.DependencyInjection;
                public class AppState { }
                public class Startup
                {
                    public void Configure(IServiceCollection services) => services.AddSingleton<AppState>();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new SingletonStateAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "DX1002");
    }
}
