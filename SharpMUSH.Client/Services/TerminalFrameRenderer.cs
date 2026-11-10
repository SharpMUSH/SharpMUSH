using System.Runtime.InteropServices;
using System.Text.Json;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;

namespace SharpMUSH.Client.Services;

/// <summary>How a received WebSocket frame should be interpreted.</summary>
public enum TerminalFrameKind
{
	/// <summary>
	/// Not an out-of-band envelope: raw server text, which may carry ANSI and VT sequences. Decode it with
	/// <see cref="TerminalFrameRenderer.DecodePlainText"/>.
	/// </summary>
	PlainText,
	/// <summary>A serialized <c>MString</c> rendered to HTML by the MarkupString library.</summary>
	Markup,
	/// <summary>Raw HTML pushed out-of-band (e.g. <c>wshtml()</c>).</summary>
	Html,
	/// <summary>Structured out-of-band data not meant for direct display.</summary>
	Oob,
	/// <summary>
	/// <c>{ "type": "prompt", "clear": true, "session": … }</c>: the prompt shown is over. A clear naming a
	/// session clears only that session's prompt; one naming none clears whatever prompt is shown.
	/// </summary>
	PromptClear
}

/// <param name="Kind">How to handle the frame.</param>
/// <param name="Plain">Plain-text content (used for correlation, buffering, accessibility).</param>
/// <param name="Html">Safe HTML for display (empty for <see cref="TerminalFrameKind.Oob"/>).</param>
/// <param name="Package">Out-of-band package identifier (empty for non-OOB frames).</param>
/// <param name="DataJson">Raw JSON text of the data payload (empty for non-OOB frames).</param>
/// <param name="Prompt">
/// A markup frame the engine marked as a prompt (<c>"prompt": true</c>: <c>@input</c>, <c>prompt()</c>,
/// <c>@prompt</c>). The terminal shows it in its prompt row instead of the scrollback.
/// </param>
/// <param name="Session">
/// The <c>@input</c> session a prompt or a prompt clear belongs to; empty for a one-off prompt, and for a
/// clear that names no session.
/// </param>
public readonly record struct TerminalFrame(TerminalFrameKind Kind, string Plain, string Html, string Package, string DataJson,
	bool Prompt = false, string Session = "");

/// <summary>One line of a plain-text frame, decoded.</summary>
/// <param name="Plain">The line's text with every sequence read out of it.</param>
/// <param name="Html">The line rendered to HTML the same way a markup frame is.</param>
public readonly record struct TerminalTextLine(string Plain, string Html);

/// <summary>
/// Interprets a raw WebSocket text frame. Game output now arrives as an out-of-band JSON envelope
/// <c>{ "type": "markup", "data": &lt;serialized MString, as an object&gt; }</c>; this deserializes the markup and
/// renders it to HTML with the MarkupString library (<c>MString.Render(MarkupFormat.Html)</c>).
/// Non-envelope frames (banners, pre-markup server text) are raw text, decoded by <see cref="DecodePlainText"/>.
/// </summary>
public static class TerminalFrameRenderer
{
	/// <summary>
	/// The terminal protocols raw server text is read with: SGR styling, the C0 controls (a carriage return
	/// overwrites, a tab moves to the next stop), DEC line drawing, and OSC titles and links.
	/// </summary>
	private static readonly DecoderRegistry PlainTextDecoders = DecoderRegistry.Empty.WithVt();

	/// <summary>
	/// The text of a plain frame, one entry a line, with its styling kept: each line is decoded to markup and
	/// rendered to HTML as a markup frame is, so it takes the same classes, colours and command links.
	/// </summary>
	/// <remarks>
	/// Each frame is decoded on its own, as the ANSI strip before it read each frame: a style left open at the
	/// end of one does not colour the next, and a last line with no line feed is still a line. The decoder's
	/// answers to terminal queries (device attributes, size) are dropped: anything sent back to a MUSH is run as
	/// a command. A line with no text (a blank line, a line of only sequences) is left out, as it was.
	/// </remarks>
	public static IReadOnlyList<TerminalTextLine> DecodePlainText(string frame)
	{
		var lines = new List<TerminalTextLine>();
		foreach (var line in Decoder.DecodeAll(frame, PlainTextDecoders))
		{
			var plain = line.Content.ToPlainText();
			if (plain.Length > 0)
				lines.Add(new TerminalTextLine(plain, line.Content.Render(MarkupFormat.Html)));
		}
		return lines;
	}

	public static TerminalFrame Parse(string frame)
	{
		var trimmed = frame.AsSpan().TrimStart();
		if (trimmed.Length == 0 || trimmed[0] != '{')
			return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty, string.Empty, string.Empty);

		try
		{
			using var doc = JsonDocument.Parse(frame);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("type", out var typeEl)
				|| typeEl.ValueKind != JsonValueKind.String)
				return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty, string.Empty, string.Empty);

			switch (typeEl.GetString())
			{
				case "markup":
					{
						// A frame from a socket owner older than this portal carries the markup as a JSON string.
						var ms = !root.TryGetProperty("data", out var markup) ? MarkupText.Empty : markup.ValueKind switch
						{
							JsonValueKind.Object => MarkupTextSerializer.Deserialize(JsonMarshal.GetRawUtf8Value(markup)),
							JsonValueKind.String => MarkupTextSerializer.Deserialize(markup.GetString()!),
							_ => MarkupText.Empty
						};
						return new TerminalFrame(TerminalFrameKind.Markup, ms.ToPlainText(), ms.Render(MarkupFormat.Html), string.Empty, string.Empty,
							IsTrue(root, "prompt"), GetStringProperty(root, "session"));
					}
				case "prompt" when IsTrue(root, "clear"):
					return new TerminalFrame(TerminalFrameKind.PromptClear, string.Empty, string.Empty, string.Empty, string.Empty,
						Session: GetStringProperty(root, "session"));
				case "html":
					{
						var data = GetStringProperty(root, "data");
						return new TerminalFrame(TerminalFrameKind.Html, data, data, string.Empty, string.Empty);
					}
				case "oob":
				case "json":
					{
						var package = GetStringProperty(root, "package");
						var dataJson = root.TryGetProperty("data", out var dataEl)
							? dataEl.GetRawText()
							: string.Empty;
						return new TerminalFrame(TerminalFrameKind.Oob, string.Empty, string.Empty, package, dataJson);
					}
				default:
					// Unknown envelope type: do not assume it was meant for us — show it as text.
					return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty, string.Empty, string.Empty);
			}
		}
		catch (JsonException)
		{
			// A plain line that merely happened to start with '{'.
			return new TerminalFrame(TerminalFrameKind.PlainText, frame, string.Empty, string.Empty, string.Empty);
		}
	}

	private static bool IsTrue(JsonElement root, string name) =>
		root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

	private static string GetStringProperty(JsonElement root, string name) =>
		root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
			? el.GetString() ?? string.Empty
			: string.Empty;
}
