using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Builds the out-of-band envelope written to WebSocket (portal) connections:
/// <c>{ "type":"oob", "package":&lt;package&gt;, "data":&lt;json-or-string&gt; }</c>.
/// The browser routes it by <c>package</c> into its OOB channel store. Mechanism only —
/// it ships whatever softcode hands it, with no room/character semantics. A package softcode sends also
/// carries <c>"from"</c>, the sender's objid; <c>sharp.*</c> packages are the engine's own.
/// <c>wsjson()</c>'s JSON goes as <c>{ "type":"json", "data":&lt;json-or-string&gt; }</c> (<see cref="BuildJson"/>),
/// PennMUSH's JSON channel: an object of its own type, with no package.
/// </summary>
public static class WebSocketOobEnvelope
{
	/// <summary>The engine's own package: the character a connection plays and the room it is in.</summary>
	public const string PlayPackage = "sharp.play";

	/// <summary>The prefix of packages only the engine sends.</summary>
	public const string ReservedPrefix = "sharp.";

	public static string Build(string package, string message) => Build(package, message, from: null);

	/// <summary>
	/// The envelope with <c>"from"</c>: the objid of the object that sent it, so the portal can tell a player's
	/// own softcode from a package someone else sent. Left out when null.
	/// </summary>
	public static string Build(string package, string message, string? from)
	{
		var envelope = new JsonObject
		{
			["type"] = "oob",
			["package"] = package,
			["data"] = Data(message)
		};
		if (from is not null) envelope["from"] = from;
		return envelope.ToJsonString();
	}

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
