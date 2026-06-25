namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// How the client authenticates to the report server. SSRS in production uses
/// NTLM/Negotiate; <see cref="Basic"/> is the practical local stand-in (and what
/// the mock exercises). <see cref="ServiceAccount"/> is a placeholder for the
/// Windows-integrated path wired in a later turn. Credentials live here, on the
/// server, and are never serialized to the browser (ADR 0010).
/// </summary>
public enum ReportCredentialMode
{
    /// <summary>No credentials are attached. Used when the server is open.</summary>
    None,

    /// <summary>HTTP Basic auth using <see cref="ReportingOptions.Username"/>/<see cref="ReportingOptions.Password"/>.</summary>
    Basic,

    /// <summary>
    /// Placeholder for Windows-integrated / service-account auth. Recognized now
    /// so the option surface is stable; the handler is added with the viewer.
    /// </summary>
    ServiceAccount,
}

/// <summary>
/// Configuration for the reporting integration: where the report server lives,
/// where its REST API lives (if different), and how to authenticate. Populated by
/// the <c>AddBlazorDXReporting(…)</c> callback at startup.
/// </summary>
public sealed class ReportingOptions
{
    /// <summary>
    /// Base URL of the SSRS URL-Access endpoint, e.g.
    /// <c>https://reports.contoso.com/ReportServer</c>. Required.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the SSRS REST API used for parameter metadata, e.g.
    /// <c>https://reports.contoso.com/reports</c>. When <c>null</c>, the parameter
    /// source falls back to <see cref="ServerUrl"/>'s host.
    /// </summary>
    public string? RestUrl { get; set; }

    /// <summary>How the client authenticates. Defaults to <see cref="ReportCredentialMode.None"/>.</summary>
    public ReportCredentialMode CredentialMode { get; set; } = ReportCredentialMode.None;

    /// <summary>Basic-auth user, used when <see cref="CredentialMode"/> is <see cref="ReportCredentialMode.Basic"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Basic-auth password, used when <see cref="CredentialMode"/> is <see cref="ReportCredentialMode.Basic"/>.</summary>
    public string? Password { get; set; }

    /// <summary>Convenience setter for Basic auth: sets the mode and credentials in one call.</summary>
    public void UseBasicAuth(string username, string password)
    {
        CredentialMode = ReportCredentialMode.Basic;
        Username = username;
        Password = password;
    }
}
