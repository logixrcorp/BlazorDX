using System.Text.Json.Serialization;
using BlazorDX.Components;

namespace BlazorDX.Demo.Client.AiChat;

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for deserializing the demo AI
/// chat broker's handshake response. <see cref="AiChat.HandleEstablishAsync"/> previously used
/// <c>JsonSerializer.Deserialize&lt;EphemeralHandshakeResult&gt;</c>'s reflection-based
/// overload, which relies on runtime reflection over <c>EphemeralHandshakeResult</c>'s
/// constructor to deserialize a positional record — metadata a trimmed Release WASM publish
/// removes. That's invisible in `dotnet build`/`dotnet run` (never trimmed) and only breaks a
/// real `dotnet publish` (what ships in production), which is exactly why this shipped
/// untested against the failure mode it actually hit: every handshake response failed to
/// deserialize with <c>NotSupportedException: DeserializeNoConstructor</c>, caught by
/// <see cref="BlazorDX.Components.SecureEphemeralChat"/>'s outer catch and surfaced as an
/// ordinary "could not be verified" decrypt failure — indistinguishable from a real one
/// without exception logging.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EphemeralHandshakeResult))]
internal sealed partial class AiChatJsonContext : JsonSerializerContext;
