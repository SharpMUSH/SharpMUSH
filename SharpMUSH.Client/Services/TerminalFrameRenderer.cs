using System.Text.Json;
using MModule = MarkupString.MarkupStringModule;

namespace SharpMUSH.Client.Services;

/// <summary>How a received WebSocket frame should be interpreted.</summary>
public enum TerminalFrameKind
{
	/// <summary>Not an out-of-band envelope — handle as raw text (with the legacy ANSI strip).</summary>
	PlainText,
	/// <summary>A serialized <c>MString</c> rendered to HTML by the MarkupString library.</summary>
	Markup,
	/// <summary>Raw HTML pushed out-of-band (e.g. <c>wshtml()</c>).</summary>
	Html,
	/// <summary>Structured out-of-band data not meant for direct display.</summary>
	Oob
}

/// <param name="Kind">How to handle the frame.</param>
/// <param name="Plain">Plain-text content (used for correlation, buffering, accessibility).</param>
/// <param name="Html">Safe HTML for display (empty for <see cref="TerminalFrameKind.Oob"/>).</param>
public readonly record struct TerminalFrame(TerminalFrameKind Kind, string Plain, string Html);

/// <summary>
/// Interprets a raw WebSocket text frame. Game output now arrives as an out-of-band JSON envelope
/// <c>{ "type": "markup", "data": &lt;serialized MString&gt; }</c>; this deserializes the markup and
/// renders it to HTML with the MarkupString library (<c>MString.Render("html")</c> →
/// <c>AnsiMarkup.WrapAsHtmlClass</c>). Non-envelope frames (banners, pre-markup server text) fall
/// back to plain-text handling.
/// </summary>
public static class TerminalFrameRenderer
{
	public static TerminalFrame Parse(string frame)
	{
		var trimmed = frame.AsSpan().TrimStart();
		if (trimmed.Length == 0 || trimmed[0] != '{')
			return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty);

		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("type", out var typeEl)
				|| typeEl.ValueKind != JsonValueKind.String)
				return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty);

			switch (typeEl.GetString())
			{
				case "markup":
				{
					var data = GetStringProperty(root, "data");
					var ms = MModule.deserialize(data);
					return new TerminalFrame(TerminalFrameKind.Markup, ms.ToPlainText(), ms.Render("html"));
				}
				case "html":
				{
					var data = GetStringProperty(root, "data");
					return new TerminalFrame(TerminalFrameKind.Html, data, data);
				}
				case "json":
					return new TerminalFrame(TerminalFrameKind.Oob, string.Empty, string.Empty);
				default:
					// Unknown envelope type: do not assume it was meant for us — show it as text.
					return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty);
			}
		}
		catch (JsonException)
		{
			// A plain line that merely happened to start with '{'.
			return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty);
		}
	}

	private static string GetStringProperty(JsonElement root, string name) =>
		root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
			? el.GetString() ?? string.Empty
			: string.Empty;
}
