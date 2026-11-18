using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Builds the out-of-band envelope written to WebSocket (portal) connections:
/// <c>{ "type":"oob", "package":&lt;package&gt;, "data":&lt;json-or-string&gt; }</c>.
/// The browser routes it by <c>package</c> into its OOB channel store. Mechanism only —
/// it ships whatever softcode hands it, with no room/character semantics.
/// <c>wsjson()</c>'s JSON goes as <c>{ "type":"json", "data":&lt;json-or-string&gt; }</c> (<see cref="BuildJson"/>),
/// PennMUSH's JSON channel: an object of its own type, with no package.
/// </summary>
public static class WebSocketOobEnvelope
{
	public static string Build(string package, string message) => new JsonObject
	{
		["type"] = "oob",
		["package"] = package,
		["data"] = Data(message)
	}.ToJsonString();

	/// <summary>The frame <c>wsjson()</c>'s JSON is sent in: <c>{ "type":"json", "data":… }</c>.</summary>
	public static string BuildJson(string message) => new JsonObject
	{
		["type"] = "json",
		["data"] = Data(message)
	}.ToJsonString();

	/// <summary><paramref name="message"/> as JSON: parsed when it parses, a JSON string when not, null when empty.</summary>
	private static JsonNode? Data(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return null;
		try
		{
			return JsonNode.Parse(message);
		}
		catch (JsonException)
		{
			return JsonValue.Create(message);
		}
	}
}
