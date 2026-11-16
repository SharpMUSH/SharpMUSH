using System.Text.Json;
using MarkupString;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The JSON <c>wsjson()</c> puts in a line, at the point it was written. A WebSocket connection is sent it as
/// an out-of-band frame and every other reader nothing (<see cref="WebSocketData"/>).
/// </summary>
/// <param name="Json">The JSON exactly as softcode gave it.</param>
public sealed record WebSocketJsonMarkup(string Json) : IPointMarkup;

/// <summary>
/// The text <c>wsjson()</c> gives a reader without a WebSocket, its <c>&lt;default&gt;</c>: shown to telnet clients,
/// listening objects and <c>think</c>, left out for a WebSocket connection, which is sent the JSON instead.
/// </summary>
/// <remarks>
/// It is data, not display (<see cref="IDataMarkup"/>): no format draws it, and stripping colour keeps it.
/// </remarks>
public sealed record WebSocketAltMarkup : IDataMarkup
{
	/// <summary>The one value; the mark carries nothing.</summary>
	public static readonly WebSocketAltMarkup Instance = new();

	private WebSocketAltMarkup() { }
}

/// <summary>Stores <see cref="WebSocketJsonMarkup"/>: its JSON as a string.</summary>
public sealed class WebSocketJsonCodec : IMarkupCodec
{
	public string Kind => "wsjson";
	public Type MarkupType => typeof(WebSocketJsonMarkup);
	public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("json", ((WebSocketJsonMarkup)markup).Json);

	public IMarkup Read(JsonElement element) => new WebSocketJsonMarkup(
		element.TryGetProperty("json", out var json) && json.ValueKind == JsonValueKind.String ? json.GetString()! : string.Empty);
}

/// <summary>Stores <see cref="WebSocketAltMarkup"/>: the kind alone.</summary>
public sealed class WebSocketAltCodec : IMarkupCodec
{
	public string Kind => "wsalt";
	public Type MarkupType => typeof(WebSocketAltMarkup);
	public void Write(Utf8JsonWriter writer, IMarkup markup) { }
	public IMarkup Read(JsonElement element) => WebSocketAltMarkup.Instance;
}

/// <summary>
/// PennMUSH's <c>wsjson()</c> markup: JSON for a WebSocket connection and a plain reading for everyone else, in one
/// value that stores, slices and joins like any other text. The engine splits it per connection before the text
/// leaves (<see cref="ForWebSocket"/>, <see cref="ForTerminal"/>), so a socket owner never sees either layer.
/// </summary>
public static class WebSocketData
{
	/// <summary>Registers both layers' codecs, so a stored <c>wsjson()</c> reads back as one.</summary>
	public static MarkupRegistry WithWebSocketData(this MarkupRegistry registry) =>
		registry.With(new WebSocketJsonCodec()).With(new WebSocketAltCodec());

	/// <summary>
	/// <paramref name="json"/> as a point, followed by <paramref name="alt"/> marked as the reading for a reader
	/// without a WebSocket. Either may be empty, and is then left out.
	/// </summary>
	public static MarkupText Build(string json, MarkupText alt)
	{
		var point = json.Length == 0 ? MarkupText.Empty : MarkupText.Point(new WebSocketJsonMarkup(json));
		var fallback = alt.Length == 0 ? MarkupText.Empty : MarkupText.Wrap(WebSocketAltMarkup.Instance, alt);
		return MarkupText.Concat(point, fallback);
	}

	/// <summary>Whether <paramref name="text"/> carries either layer.</summary>
	public static bool Has(MarkupText text)
	{
		if (text.Runs.IsDefaultOrEmpty) return false;
		foreach (var run in text.Runs)
			foreach (var markup in run.Markups)
				if (markup is WebSocketJsonMarkup or WebSocketAltMarkup) return true;
		return false;
	}

	/// <summary>
	/// What a WebSocket connection is shown: the text without any <c>&lt;default&gt;</c>, and the JSON of each point
	/// in the order it was written.
	/// </summary>
	public static (MarkupText Text, IReadOnlyList<string> Json) ForWebSocket(MarkupText text)
	{
		var json = new List<string>();
		return (Split(text, json), json);
	}

	/// <summary>What every other reader is shown: the text with each <c>&lt;default&gt;</c> and no JSON.</summary>
	public static MarkupText ForTerminal(MarkupText text) => Split(text, null);

	/// <summary>
	/// <paramref name="text"/> without its JSON points, collected into <paramref name="json"/> when it is given. With
	/// <paramref name="json"/> the text marked as a <c>&lt;default&gt;</c> goes too; without it the text stays and only
	/// the mark goes.
	/// </summary>
	private static MarkupText Split(MarkupText text, List<string>? json)
	{
		if (!Has(text)) return text;

		var parts = new List<MarkupText>(text.Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in text.Runs)
		{
			var point = run.Markups.OfType<WebSocketJsonMarkup>().FirstOrDefault();
			var alt = run.Markups.Contains(WebSocketAltMarkup.Instance);
			if (point is null && !alt) continue;

			if (run.Start > position) parts.Add(text.Substring(position, run.Start - position));
			position = run.End;
			if (point is not null)
			{
				// A run of identical points is one run over several carriers: one object a carrier.
				for (var i = 0; i < run.Length; i++) json?.Add(point.Json);
				continue;
			}
			if (json is null) parts.Add(WithoutAlt(text.Substring(run.Start, run.Length)));
		}
		if (position < text.Length) parts.Add(text.Substring(position));
		return MarkupText.Concat(parts);
	}

	private static MarkupText WithoutAlt(MarkupText text)
	{
		var runs = new List<Run>(text.Runs.Length);
		foreach (var run in text.Runs)
		{
			var kept = run.Markups.Where(markup => markup is not WebSocketAltMarkup).ToArray();
			if (kept.Length > 0) runs.Add(run with { Markups = MarkupSet.Of(kept) });
		}
		return MarkupText.Create(text.Text, runs.ToArray());
	}
}
