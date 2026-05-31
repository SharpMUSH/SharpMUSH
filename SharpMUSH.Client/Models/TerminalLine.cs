namespace SharpMUSH.Client.Models;

public class TerminalLine
{
	public TerminalLine(DateTime timestamp, string text, TerminalLineSource source)
	{
		Timestamp = timestamp;
		Text = text;
		Source = source;
	}

	public DateTime Timestamp { get; }
	public string Text { get; }
	public TerminalLineSource Source { get; }
}

public enum TerminalLineSource
{
	Server,
	Client,
	System
}
