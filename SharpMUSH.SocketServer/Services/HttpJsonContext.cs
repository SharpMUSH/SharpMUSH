using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.SocketServer.Services;

/// <summary>What <c>/health</c> and <c>/ready</c> answer.</summary>
public sealed record HealthReply(string Status, DateTimeOffset Timestamp);

/// <summary>
/// The JSON the socket server and the rendering worker exchange over HTTP, and the socket server's own health
/// replies, with ASP.NET Core's web defaults, which both <c>PostAsJsonAsync</c> and a minimal API's body use.
/// Generated, so both run compiled ahead of time.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RenderRequest))]
[JsonSerializable(typeof(HealthReply))]
public sealed partial class HttpJsonContext : JsonSerializerContext;
