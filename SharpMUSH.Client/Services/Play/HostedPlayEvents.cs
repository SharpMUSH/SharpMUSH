using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Client.Services.Play;

/// <summary>
/// Play events as a hosted application's frame receives them in a <c>play-event</c> message (bridge version 1). Each is
/// an object with a <c>kind</c>: <c>reset</c>, <c>state</c> or <c>package</c>. A frame skips kinds and fields it does
/// not know; a field is never removed or given another meaning without raising the message's <c>v</c>.
/// </summary>
public static class HostedPlayEvents
{
	/// <summary>The event as JSON, or null for an event of a kind the bridge does not carry.</summary>
	public static JsonObject? ToJson(PlayEvent item) => item switch
	{
		PlayReset reset => new JsonObject
		{
			["kind"] = "reset",
			["generation"] = reset.Generation,
			["reason"] = Name(reset.Reason)
		},
		PlayStateChanged changed => new JsonObject
		{
			["kind"] = "state",
			["generation"] = changed.Generation,
			["previous"] = ToJson(changed.Previous),
			["current"] = ToJson(changed.Current)
		},
		PlayPackage package => new JsonObject
		{
			["kind"] = "package",
			["generation"] = package.Generation,
			["package"] = package.Package,
			["version"] = package.Version,
			["data"] = package.Data.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(package.Data.GetRawText()),
			["origin"] = Name(package.Origin),
			["sender"] = Name(package.Sender),
			["senderObjId"] = package.SenderObjId
		},
		_ => null
	};

	/// <summary>
	/// The state as JSON: <c>{"generation","connection","room","character":{"objId","name","guest"}}</c>, the character
	/// null when nobody is playing.
	/// </summary>
	public static JsonObject ToJson(PlayState state) => new()
	{
		["generation"] = state.Generation,
		["connection"] = Name(state.Connection),
		["room"] = state.Room,
		["character"] = state.Character is { } character
			? new JsonObject
			{
				["objId"] = character.ObjId,
				["name"] = character.Name,
				["guest"] = character.Guest
			}
			: null
	};

	/// <summary>An enum value as the bridge names it: camel case (<c>characterChanged</c>).</summary>
	private static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
