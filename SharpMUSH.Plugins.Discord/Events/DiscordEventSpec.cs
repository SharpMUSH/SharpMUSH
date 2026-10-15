using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Plugins.Discord.Events;

/// <summary>
/// What softcode publishes as a Discord external Scheduled Event (<c>@discord/event &lt;key&gt;=&lt;json&gt;</c>). Discord
/// requires a location and an end time on an external event; names and locations run to 100 characters and
/// descriptions to 1000, so longer ones are cut with an ellipsis.
/// </summary>
/// <param name="GuildId">The server to publish in; null for the one server the bot is in.</param>
public sealed record DiscordEventSpec(
	string Name,
	DateTimeOffset Start,
	DateTimeOffset End,
	string Location,
	string? Description,
	ulong? GuildId)
{
	public const int MaxName = 100;
	public const int MaxLocation = 100;
	public const int MaxDescription = 1000;

	/// <summary>How long an event lasts when no end is given.</summary>
	public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(3);

	/// <summary>
	/// Reads <c>{"name", "start", "end", "location", "description", "server"}</c>: <c>start</c> and <c>end</c> in seconds since
	/// 1970 UTC, as <c>secs()</c> gives them, and <c>server</c> a server id. <c>name</c>, <c>start</c> and <c>location</c>
	/// are required; <c>start</c> must be still to come.
	/// </summary>
	public static Result<DiscordEventSpec> Parse(string json, DateTimeOffset now)
	{
		JsonElement root;
		try
		{
			using var document = JsonDocument.Parse(json);
			root = document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return new Error<string>("The event must be a JSON object, such as {\"name\":\"Harbour Night\",\"start\":1792000000,\"location\":\"https://game.example/scenes/12\"}.");
		}

		if (root.ValueKind != JsonValueKind.Object)
		{
			return new Error<string>("The event must be a JSON object.");
		}

		if (Text(root, "name") is not { Length: > 0 } name)
		{
			return new Error<string>("The event needs a name.");
		}

		if (Text(root, "location") is not { Length: > 0 } location)
		{
			return new Error<string>("The event needs a location: Discord requires one on an event outside a voice channel.");
		}

		if (Seconds(root, "start") is not { } start)
		{
			return new Error<string>("The event needs a start, in seconds since 1970 UTC, as secs() gives it.");
		}

		if (start <= now)
		{
			return new Error<string>("The event's start has passed. Discord takes only events still to come.");
		}

		// An end left out, or empty, is the default length; one given that is not a time is refused, not replaced.
		if (Text(root, "end") is { Length: > 0 } && Seconds(root, "end") is null)
		{
			return new Error<string>("The event's end must be in seconds since 1970 UTC, as secs() gives it.");
		}

		var end = Seconds(root, "end") ?? start + DefaultLength;
		if (end <= start)
		{
			return new Error<string>("The event's end must come after its start.");
		}

		ulong? guild = null;
		if (Text(root, "server") is { Length: > 0 } server)
		{
			if (!ulong.TryParse(server, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
			{
				return new Error<string>("server takes a Discord server's id.");
			}

			guild = id;
		}

		return new DiscordEventSpec(Cut(name, MaxName), start, end, Cut(location, MaxLocation),
			Text(root, "description") is { Length: > 0 } description ? Cut(description, MaxDescription) : null, guild);
	}

	private static string? Text(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value)
			? value.ValueKind switch
			{
				JsonValueKind.String => value.GetString()?.Trim(),
				JsonValueKind.Number => value.GetRawText(),
				_ => null,
			}
			: null;

	private static DateTimeOffset? Seconds(JsonElement root, string name)
		=> Text(root, name) is { } text && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds)
			&& seconds is > 0 and < 253402300800
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;

	private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
