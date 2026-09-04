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

    // ---- DX1003: hardcoded user-facing text in an already-localized component ----

    // Enough of the Blazor / DxStrings surface for the analyzer's semantic checks to bind.
    private const string LocalizationPreamble = """
        namespace Microsoft.AspNetCore.Components.Rendering
        {
            public sealed class RenderTreeBuilder
            {
                public void AddContent(int sequence, object? value) { }
                public void AddAttribute(int sequence, string name, object? value) { }
            }
        }

        namespace Microsoft.AspNetCore.Components
        {
            public sealed class ParameterAttribute : System.Attribute { }
        }

        namespace BlazorDX.Components
        {
            internal sealed class DxStrings<T>
            {
                public string this[string key, string english] => english;
            }
        }
        """;

    private static string LocalizedComponent(string body) => $$"""
        {{LocalizationPreamble}}

        namespace Test
        {
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;
            using BlazorDX.Components;

            public sealed class DxThing
            {
                private DxStrings<DxThing> S = new();
        {{body}}
            }
        }
        """;

    [Fact]
    public async Task DX1003_fires_for_a_hardcoded_aria_label_in_a_localized_component()
    {
        string source = LocalizedComponent("""
                public void Render(RenderTreeBuilder builder) =>
                    builder.AddAttribute(1, "aria-label", "Dismiss");
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "DX1003");
    }

    [Fact]
    public async Task DX1003_fires_for_hardcoded_visible_content_and_interpolated_prose()
    {
        string source = LocalizedComponent("""
                public void Render(RenderTreeBuilder builder, int count)
                {
                    builder.AddContent(1, "Select all rows");
                    builder.AddContent(2, $"Chart of {count} points");
                }
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Equal(2, diagnostics.Count(d => d.Id == "DX1003"));
    }

    [Fact]
    public async Task DX1003_fires_for_a_defaulted_Parameter_string()
    {
        string source = LocalizedComponent("""
                [Parameter] public string SubmitText { get; set; } = "Submit";
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "DX1003");
    }

    [Fact]
    public async Task DX1003_ignores_a_defaulted_Parameter_that_names_a_variant_not_text()
    {
        // The regression this locks in: the rule originally flagged every defaulted [Parameter]
        // string, so DxAlert's own `Severity = "info"` -- a token that ends up in a CSS class --
        // broke the build. A variant, a size and a format are all machine-facing; only a parameter
        // whose name says it carries text ("...Label", "...Text", ...) is the component's own prose.
        string source = LocalizedComponent("""
                [Parameter] public string Severity { get; set; } = "info";
                [Parameter] public string Size { get; set; } = "md";
                [Parameter] public string DateFormat { get; set; } = "yyyy-MM-dd";
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Empty(diagnostics.Where(d => d.Id == "DX1003"));
    }

    [Fact]
    public async Task DX1003_stays_silent_for_text_routed_through_the_localizer()
    {
        string source = LocalizedComponent("""
                public void Render(RenderTreeBuilder builder)
                {
                    builder.AddAttribute(1, "aria-label", S["Dismiss", "Dismiss"]);
                    builder.AddContent(2, $"{S["Copy", "Copy"]} ▾");
                }
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Empty(diagnostics.Where(d => d.Id == "DX1003"));
    }

    [Fact]
    public async Task DX1003_ignores_machine_facing_attributes_glyphs_and_formats()
    {
        // class/style/role/type are machine-facing; glyphs and format strings carry no language.
        string source = LocalizedComponent("""
                public void Render(RenderTreeBuilder builder, double value)
                {
                    builder.AddAttribute(1, "class", "dx-alert dx-alert-info");
                    builder.AddAttribute(2, "role", "status");
                    builder.AddAttribute(3, "type", "button");
                    builder.AddContent(4, "✕");
                    builder.AddContent(5, " *");
                    builder.AddContent(6, value.ToString("0.#"));
                }
        """);

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Empty(diagnostics.Where(d => d.Id == "DX1003"));
    }

    [Fact]
    public async Task DX1003_stays_silent_for_a_component_that_does_not_localize_yet()
    {
        // The ratchet: the not-yet-localized backlog must not break the build. Localizing a
        // component is what switches the rule on for it.
        string source = $$"""
            {{LocalizationPreamble}}

            namespace Test
            {
                using Microsoft.AspNetCore.Components.Rendering;

                public sealed class DxNotYetLocalized
                {
                    public void Render(RenderTreeBuilder builder) =>
                        builder.AddAttribute(1, "aria-label", "Dismiss");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(source, new HardcodedStringAnalyzer());

        Assert.Empty(diagnostics.Where(d => d.Id == "DX1003"));
    }
}
