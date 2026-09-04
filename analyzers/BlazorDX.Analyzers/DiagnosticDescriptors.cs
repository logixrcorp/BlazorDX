using Microsoft.CodeAnalysis;

namespace BlazorDX.Analyzers;

/// <summary>
/// Central registry of every diagnostic BlazorDX's analyzers can report.
/// Keeping the descriptors in one place makes the full rule set reviewable at
/// a glance and keeps IDs from colliding.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string ReadabilityCategory = "BlazorDX.Readability";
    private const string SecurityCategory = "BlazorDX.Security";
    private const string GlobalizationCategory = "BlazorDX.Globalization";

    /// <summary>DX1000 — a source file exceeds the 1000-line cap.</summary>
    public static readonly DiagnosticDescriptor FileTooLong = new(
        id: "DX1000",
        title: "Source file exceeds the 1000-line cap",
        messageFormat: "'{0}' has {1} lines; the BlazorDX cap is {2}. Split it by responsibility.",
        category: ReadabilityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "BlazorDX caps every source file at 1000 lines to keep the codebase human-readable.");

    /// <summary>DX1001 — raw HTML built from runtime data (an XSS hazard).</summary>
    public static readonly DiagnosticDescriptor RawHtmlInjection = new(
        id: "DX1001",
        title: "Raw HTML must be sanitized",
        messageFormat: "MarkupString is built from non-constant data here; route it through BlazorDX.Security's sanitizer",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Constructing MarkupString from runtime data bypasses Blazor's auto-encoding and risks XSS.");

    /// <summary>DX1002 — UI state registered as a Singleton (cross-user leakage).</summary>
    public static readonly DiagnosticDescriptor SingletonState = new(
        id: "DX1002",
        title: "UI state should not be a Singleton",
        messageFormat: "'{0}' looks like UI state registered as a Singleton; on Blazor Server this is shared across all users. Use a scoped lifetime.",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Singleton UI state is shared across every connected circuit on Blazor Server, leaking data between users.");

    /// <summary>DX1003 — a hardcoded user-facing string in an already-localized component.</summary>
    public static readonly DiagnosticDescriptor HardcodedUserFacingString = new(
        id: "DX1003",
        title: "User-facing text must go through the component's localizer",
        messageFormat: "\"{0}\" is user-facing text in a localized component; route it through this component's DxStrings field (S[\"Key\", \"{0}\"])",
        category: GlobalizationCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A component that already localizes some of its text should not leave other user-facing "
            + "text hardcoded, or translations are silently partial. Only components holding a DxStrings<T> "
            + "field are checked, so localizing a component is what switches this rule on for it.");
}
