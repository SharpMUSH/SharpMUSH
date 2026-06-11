using System.Net;

namespace SharpMUSH.Client.Models;

public class TerminalLine
{
	/// <summary>
	/// Creates a line whose HTML rendering is the HTML-encoded plain text (for system/local/plain
	/// server lines that carry no markup).
	/// </summary>
	public TerminalLine(DateTime timestamp, string text, TerminalLineSource source)
		: this(timestamp, text, WebUtility.HtmlEncode(text), source)
	{
	}

	/// <summary>
	/// Creates a line with an explicit, already-safe HTML rendering (used for markup server lines
	/// rendered from an <c>MString</c> via the MarkupString HTML renderer).
	/// </summary>
	public TerminalLine(DateTime timestamp, string text, string html, TerminalLineSource source)
	{
		Timestamp = timestamp;
		Text = text;
		Html = html;
		Source = source;
	}

	public DateTime Timestamp { get; }

	/// <summary>The plain (unstyled) text of the line — used for correlation, copy, and accessibility.</summary>
	public string Text { get; }

	/// <summary>Safe HTML for display. Always HTML-encoded or produced by a trusted markup renderer.</summary>
	public string Html { get; }

	public TerminalLineSource Source { get; }
}

public enum TerminalLineSource
{
	Server,
	Client,
	System
}
