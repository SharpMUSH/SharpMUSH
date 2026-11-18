using System.Text.Json;
using MarkupString;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// <c>wsjson()</c> over its <c>&lt;default&gt;</c>: the text a reader without a WebSocket is shown (telnet clients,
/// listening objects, <c>think</c>, string functions), carrying the JSON a WebSocket connection is sent instead
/// (<see cref="WebSocketData"/>). Because it marks the default rather than standing beside it, string functions
/// count the default's characters and nothing else.
/// </summary>
/// <remarks>
/// It is data, not display (<see cref="IDataMarkup"/>): no format draws it, and stripping colour keeps it.
/// </remarks>
/// <param name="Json">The JSON exactly as softcode gave it; empty when there was none.</param>
public sealed record WebSocketJsonMarkup(string Json) : IDataMarkup;

/// <summary>
/// <c>wsjson()</c> with no <c>&lt;default&gt;</c>: the JSON at the point it was written, which no reader but a
/// WebSocket connection gets anything for.
/// </summary>
/// <param name="Json">The JSON exactly as softcode gave it.</param>
public sealed record WebSocketJsonPointMarkup(string Json) : IPointMarkup;

/// <summary>Stores <see cref="WebSocketJsonMarkup"/>: its JSON as a string.</summary>
public sealed class WebSocketJsonCodec : IMarkupCodec
{
	public string Kind => "wsjson";
	public Type MarkupType => typeof(WebSocketJsonMarkup);
	public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("json", ((WebSocketJsonMarkup)markup).Json);
	public IMarkup Read(JsonElement element) => new WebSocketJsonMarkup(WebSocketData.ReadJson(element));
}

/// <summary>Stores <see cref="WebSocketJsonPointMarkup"/>: its JSON as a string.</summary>
public sealed class WebSocketJsonPointCodec : IMarkupCodec
{
	public string Kind => "wsjsonpoint";
	public Type MarkupType => typeof(WebSocketJsonPointMarkup);
	public void Write(Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("json", ((WebSocketJsonPointMarkup)markup).Json);
	public IMarkup Read(JsonElement element) => new WebSocketJsonPointMarkup(WebSocketData.ReadJson(element));
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
		registry.With(new WebSocketJsonCodec()).With(new WebSocketJsonPointCodec());

	internal static string ReadJson(JsonElement element) =>
		element.TryGetProperty("json", out var json) && json.ValueKind == JsonValueKind.String ? json.GetString()! : string.Empty;

	/// <summary>
	/// <paramref name="alt"/> carrying <paramref name="json"/>, or <paramref name="json"/> as a point when there is no
	/// <paramref name="alt"/>; empty when both are. A <c>wsjson()</c> inside <paramref name="alt"/> is reduced to its
	/// own <c>&lt;default&gt;</c>, as PennMUSH sends a WebSocket nothing of a default.
	/// </summary>
	public static MarkupText Build(string json, MarkupText alt)
	{
		var fallback = ForTerminal(alt);
		if (fallback.Length > 0) return MarkupText.Wrap(new WebSocketJsonMarkup(json), fallback);
		return json.Length == 0 ? MarkupText.Empty : MarkupText.Point(new WebSocketJsonPointMarkup(json));
	}

	/// <summary>Whether <paramref name="text"/> carries either layer.</summary>
	public static bool Has(MarkupText text)
	{
		if (text.Runs.IsDefaultOrEmpty) return false;
		foreach (var run in text.Runs)
			foreach (var markup in run.Markups)
				if (markup is WebSocketJsonMarkup or WebSocketJsonPointMarkup) return true;
		return false;
	}

	/// <summary>
	/// What a WebSocket connection is shown: the text without any <c>&lt;default&gt;</c>, and each <c>wsjson()</c>'s
	/// JSON in the order it was written.
	/// </summary>
	public static (MarkupText Text, IReadOnlyList<string> Json) ForWebSocket(MarkupText text)
	{
		var json = new List<string>();
		return (Split(text, json), json);
	}

	/// <summary>What every other reader is shown: the text with each <c>&lt;default&gt;</c> and no JSON.</summary>
	public static MarkupText ForTerminal(MarkupText text) => Split(text, null);

	/// <summary>
	/// <paramref name="text"/> without its JSON. With <paramref name="json"/> each <c>&lt;default&gt;</c> goes and its
	/// JSON is collected, once for each stretch of text it marks; without it the text stays and only the mark goes.
	/// Ranges are copied exactly, never snapped to a grapheme, so a combining mark after a removed run stays put.
	/// </summary>
	private static MarkupText Split(MarkupText text, List<string>? json)
	{
		if (!Has(text)) return text;

		var kept = new System.Text.StringBuilder(text.Length);
		var runs = new List<Run>(text.Runs.Length);
		var position = 0;
		WebSocketJsonMarkup? previous = null;
		var previousEnd = -1;
		foreach (var run in text.Runs)
		{
			var marked = run.Markups.OfType<WebSocketJsonMarkup>().FirstOrDefault();
			var point = run.Markups.OfType<WebSocketJsonPointMarkup>().FirstOrDefault();
			var continues = marked is not null && previousEnd == run.Start && marked.Equals(previous);
			previous = marked;
			previousEnd = run.End;

			if (point is not null)
			{
				Gap(run.Start);
				position = run.End;
				// A run of identical points is one run over several carriers: one object a carrier.
				if (json is not null)
					for (var i = 0; i < run.Length; i++) json.Add(point.Json);
				continue;
			}
			if (marked is null || json is null)
			{
				Gap(run.Start);
				var markups = run.Markups.Where(markup => markup is not WebSocketJsonMarkup).ToArray();
				if (markups.Length > 0) runs.Add(new Run(kept.Length, run.Length, MarkupSet.Of(markups)));
				kept.Append(text.Text, run.Start, run.Length);
				position = run.End;
				continue;
			}

			Gap(run.Start);
			position = run.End;
			if (!continues && marked.Json.Length > 0) json.Add(marked.Json);
		}
		Gap(text.Length);
		return MarkupText.Create(kept.ToString(), runs.ToArray());

		void Gap(int until)
		{
			if (until > position) kept.Append(text.Text, position, until - position);
		}
	}
}
