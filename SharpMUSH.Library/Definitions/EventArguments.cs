using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The names an event handler's attribute reads its arguments by, as %&lt;name&gt;, beside %0-%9. Each
/// event's names are in the order of its arguments, as <c>help event list</c> gives them; PennMUSH's
/// events keep their positions, so code written for PennMUSH still reads %0-%9.
/// </summary>
public static class EventArguments
{
	/// <summary>
	/// A name holding <c>/</c> names the parts of a value that is several numbers joined by <c>/</c>
	/// (PennMUSH's <c>recv/sent/cmds</c>), so each part has a name of its own.
	/// </summary>
	private static readonly Dictionary<string, string[]> Names = new(StringComparer.OrdinalIgnoreCase)
	{
		["DUMP`5MIN"] = ["message", "forking"],
		["DUMP`1MIN"] = ["message", "forking"],
		["DUMP`COMPLETE"] = ["message", "forking"],
		["DUMP`ERROR"] = ["message", "forking", "status"],
		["LOG`ERR"] = ["message"],
		["LOG`CMD"] = ["message"],
		["LOG`WIZ"] = ["message"],
		["LOG`CONN"] = ["message"],
		["LOG`TRACE"] = ["message"],
		["LOG`CHECK"] = ["message"],
		["LOG`HUH"] = ["message"],
		["OBJECT`CREATE"] = ["objid", "clone"],
		["OBJECT`DESTROY"] = ["objid", "name", "type", "owner", "parent", "zone"],
		["OBJECT`MOVE"] = ["objid", "destination", "origin", "silent", "cause"],
		["OBJECT`RENAME"] = ["objid", "name", "oldname"],
		["OBJECT`FLAG"] = ["objid", "flag", "type", "set", "change"],
		["SQL`CONNECT"] = ["platform"],
		["SQL`CONNECTFAIL"] = ["platform", "error"],
		["SQL`DISCONNECT"] = ["platform", "error"],
		["PLAYER`CREATE"] = ["objid", "name", "how", "descriptor", "email"],
		["PLAYER`CONNECT"] = ["objid", "connections", "descriptor"],
		["PLAYER`DISCONNECT"] =
		[
			"objid", "connections", "hidden", "cause", "ip", "descriptor", "connected", "idle",
			"received/sent/commands"
		],
		[SharpEvents.PlayerChannels] = ["objid", "cause", "channel"],
		["PLAYER`WARN"] = ["objid", "reason", "account"],
		["SOCKET`CONNECT"] = ["descriptor", "ip"],
		["SOCKET`DISCONNECT"] = ["descriptor", "ip", "cause", "received/sent/commands"],
		["SOCKET`LOGINFAIL"] = ["descriptor", "ip", "count", "reason", "objid", "name"],
		["SOCKET`CREATEFAIL"] = ["descriptor", "ip", "count", "reason", "name", "error"],
		["SOCKET`GMCP"] = ["descriptor", "package", "message", "player"],
		["HTTP`BLOCKED"] = ["descriptor", "ip", "method", "path", "reason"],
		["HTTP`FAIL"] = ["descriptor", "ip", "reason"],
		["HTTP`COMMAND"] = ["ip", "method", "path", "status", "content-type", "request-length", "response-length"],
		[SharpEvents.RoomContents] = ["room", "cause"],
		[SharpEvents.ChannelMessage] = ["channel", "speaker", "style", "name", "message", "recipients", "time", "id"],
		[SharpEvents.ChannelWho] = ["channel", "member", "name", "change", "viewers", "cause"],
		[SharpEvents.PageMessage] = ["speaker", "recipients", "style", "name", "message", "time", "id"],
	};

	/// <summary>The names <paramref name="eventName"/>'s arguments are passed under, in order; none for an event not listed.</summary>
	public static IReadOnlyList<string> For(string eventName)
		=> Names.TryGetValue(eventName, out var names) ? names : [];

	/// <summary>
	/// <paramref name="arguments"/> as an event handler receives them: by position as %0-%9, and by the
	/// event's names.
	/// </summary>
	public static Dictionary<string, CallState> Of(string eventName, IReadOnlyList<string> arguments)
	{
		var named = arguments.Index().ToDictionary(x => x.Index.ToString(), x => new CallState(x.Item), StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value) in For(eventName).Zip(arguments))
		{
			foreach (var (part, partValue) in Parts(name, value))
			{
				named[part] = new CallState(partValue);
			}
		}

		return named;
	}

	private static IEnumerable<(string Name, string Value)> Parts(string name, string value)
	{
		if (!name.Contains('/'))
		{
			return [(name, value)];
		}

		var values = value.Split('/');
		return name.Split('/').Select((part, index) => (part, index < values.Length ? values[index] : string.Empty));
	}
}
