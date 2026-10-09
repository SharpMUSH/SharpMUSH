using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Pages;

/// <summary>The Softcode Editor console's input line and the expressions typed into it, for Up and Down.</summary>
/// <remarks>
/// Kept in memory only, for as long as the page is open: an expression can carry a secret anywhere in it
/// (checkpass(me,...), decrypt(...,key)), which no rule about a leading verb catches, so none of it goes to
/// localStorage as the terminal's does.
/// </remarks>
public sealed class SoftcodeConsoleLine
{
	// Typed expressions, oldest first.
	private readonly List<string> _history = [];
	private readonly HistoryWalk _walk = new();

	/// <summary>What the line holds now.</summary>
	public string Text { get; private set; } = string.Empty;

	/// <summary>The reader typed; a walk through the history starts again from here.</summary>
	public void Type(string text)
	{
		Text = text;
		_walk.Reset();
	}

	/// <summary>Up: the previous expression matching what was typed, when there is one.</summary>
	public void Older()
	{
		if (_walk.Older(_history, Text) is { } older) Text = older;
	}

	/// <summary>Down: the next expression, or what was typed past the newest.</summary>
	public void Newer()
	{
		if (_walk.Newer(_history) is { } newer) Text = newer;
	}

	/// <summary>
	/// The expression to evaluate, trimmed; the line empties and remembers it. An empty line gives an empty
	/// string and is left as it is.
	/// </summary>
	public string Take()
	{
		var line = Text.Trim();
		if (line.Length == 0) return line;

		Text = string.Empty;
		_walk.Reset();
		Remember(line);
		return line;
	}

	private void Remember(string line)
	{
		if (_history.Count > 0 && _history[^1] == line) return;
		_history.Add(line);
		if (_history.Count > CommandHistory.Capacity) _history.RemoveAt(0);
	}
}
