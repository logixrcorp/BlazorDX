namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Configuration for the Power BI embedding integration: where the Power BI REST
/// API lives and which workspace (group) the reports belong to. Populated by the
/// <c>AddBlazorDXPowerBi(…)</c> callback at startup. Note there are NO secrets
/// here: the Azure AD token is supplied per request by an
/// <see cref="IPowerBiTokenProvider"/>, so credentials never sit in options that
/// might be logged or serialized (ADR 0010).
/// </summary>
public sealed class PowerBiOptions
{
    /// <summary>
    /// Base URL of the Power BI REST API. Defaults to the public service
    /// <c>https://api.powerbi.com</c>; override it to point at the mock or a
    /// sovereign cloud. The <c>/v1.0/myorg/...</c> path is appended by the service.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.powerbi.com";

    /// <summary>
    /// The Power BI workspace (group) GUID the reports live in — the
    /// <c>{groupId}</c> in <c>groups/{groupId}/reports/{reportId}</c>. Required.
    /// </summary>
    public string WorkspaceId { get; set; } = string.Empty;
}
