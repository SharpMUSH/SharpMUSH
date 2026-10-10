using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What a terminal keeps of its screen for a reload (<see cref="TerminalResumeSlot.AppendLine"/>): the
/// server's rendered lines, up to <see cref="MaxLines"/> lines or <see cref="MaxChars"/> characters.
/// </summary>
/// <remarks>
/// <para>Only rendered text is kept. An out-of-band frame never becomes a line, so restoring the screen
/// cannot run one again; the page gets current state from the snapshots a resume re-sends.</para>
/// <para>A line's actions are dropped from its HTML: a sound, a stop, a clear screen, an expiry, a
/// prefetch. They happened when the line arrived; a restored screen shows what was said, it does not
/// play it again. A line that was only an action is not kept at all.</para>
/// <para>Only the server's lines are kept. What the player typed can carry a password (<c>connect</c>,
/// <c>@password</c>), and sessionStorage outlives the page; system lines describe the page's own
/// connection, which a reload replaces.</para>
/// </remarks>
public static partial class TerminalScrollback
{
	public const int MaxLines = 500;

	/// <summary>Two terminals' worth stays well inside a browser's sessionStorage quota.</summary>
	public const int MaxChars = 256 * 1024;

	public static Found<string> Serialize(TerminalLine line)
	{
		if (line.Source != TerminalLineSource.Server) return new NotFound();

		var html = Inert(line.Html);
		if (string.IsNullOrWhiteSpace(line.Text) && !HasVisibleElement(html)) return new NotFound();

		return JsonSerializer.Serialize(new StoredLine(line.Text, html, new DateTimeOffset(line.Timestamp).ToUnixTimeMilliseconds()));
	}

	public static IReadOnlyList<TerminalLine> Parse(string? stored)
	{
		if (string.IsNullOrEmpty(stored)) return [];
		try
		{
			return JsonSerializer.Deserialize<StoredLine[]>(stored) is { } lines
				? [.. lines.Where(l => l.T is not null).Select(l => new TerminalLine(
					DateTimeOffset.FromUnixTimeMilliseconds(l.At).LocalDateTime, l.T!, Inert(l.H ?? string.Empty), TerminalLineSource.Server))]
				: [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	/// <summary>
	/// The prompt a terminal shows, for a reload: its line kept as a scrollback line is, with its session.
	/// A prompt with nothing to show is not kept.
	/// </summary>
	public static Found<string> SerializePrompt(TerminalPrompt prompt) =>
		Serialize(prompt.Line) switch
		{
			string line => $"{{\"s\":{JsonSerializer.Serialize(prompt.Session)},\"l\":{line}}}",
			_ => new NotFound()
		};

	public static Found<TerminalPrompt> ParsePrompt(string? stored)
	{
		if (string.IsNullOrEmpty(stored)) return new NotFound();
		try
		{
			return JsonSerializer.Deserialize<StoredPrompt>(stored) is { L.T: { } text } kept
				? new TerminalPrompt(new TerminalLine(DateTimeOffset.FromUnixTimeMilliseconds(kept.L.At).LocalDateTime, text,
					Inert(kept.L.H ?? string.Empty), TerminalLineSource.Server), kept.S ?? string.Empty)
				: new NotFound();
		}
		catch (JsonException)
		{
			return new NotFound();
		}
	}

	/// <summary>The HTML without the elements that act when rendered.</summary>
	private static string Inert(string html) => EmptyActionElement().Replace(MediaOrLink().Replace(html, string.Empty), string.Empty);

	private static bool HasVisibleElement(string html) => html.Contains("<img", StringComparison.OrdinalIgnoreCase);

	[GeneratedRegex(@"<(audio|video)\b[^>]*>.*?</\1\s*>|<(audio|video|link)\b[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex MediaOrLink();

	// The markup renderer's points with nothing to show: a sound, its stop, a bell, a clear screen, an expiry.
	// Only these: an empty table cell or text block (ms-nowrap, ms-text) holds its place in a layout.
	[GeneratedRegex(@"<(\w+)\b[^>]*\bclass=""ms-(?:sound|sound-stop|bell|clear|expire)(?:\s[^""]*)?""[^>]*>\s*</\1\s*>", RegexOptions.IgnoreCase)]
	private static partial Regex EmptyActionElement();

	private sealed record StoredPrompt(
		[property: System.Text.Json.Serialization.JsonPropertyName("s")] string? S,
		[property: System.Text.Json.Serialization.JsonPropertyName("l")] StoredLine L);

	private sealed record StoredLine(
		[property: System.Text.Json.Serialization.JsonPropertyName("t")] string? T,
		[property: System.Text.Json.Serialization.JsonPropertyName("h")] string? H,
		[property: System.Text.Json.Serialization.JsonPropertyName("at")] long At);
}
