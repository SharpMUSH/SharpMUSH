using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Builds the out-of-band envelope written to WebSocket (portal) connections:
/// <c>{ "type":"oob", "package":&lt;package&gt;, "data":&lt;json-or-string&gt; }</c>.
/// The browser routes it by <c>package</c> into its OOB channel store. Mechanism only —
/// it ships whatever softcode hands it, with no room/character semantics.
/// </summary>
public static class WebSocketOobEnvelope
{
	public static string Build(string package, string message)
	{
		JsonNode? data;
		if (string.IsNullOrWhiteSpace(message))
		{
			data = null;
		}
		else
		{
			try
			{
				data = JsonNode.Parse(message);
			}
			catch (JsonException)
			{
				data = JsonValue.Create(message);
			}
		}

		var envelope = new JsonObject
		{
			["type"] = "oob",
			["package"] = package,
			["data"] = data
		};

		return envelope.ToJsonString();
	}
}
