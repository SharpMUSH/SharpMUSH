namespace SharpMUSH.Client.Pages;

/// <summary>One evaluation in the Softcode Editor's console: what ran, as whom, and what came back.</summary>
/// <param name="Source">The expression typed, or for a run of the buffer the attribute it holds (<c>#8/FN`GREET</c>).</param>
/// <param name="IsBuffer">Whether this ran the editor's buffer rather than a typed expression.</param>
/// <param name="Unsaved">Whether the buffer that ran differed from what the attribute holds.</param>
/// <param name="Arguments">What <c>%0</c> onward were, as typed.</param>
/// <param name="Output">Everything the character was told while it ran.</param>
/// <param name="Result">The value, or null when it did not run.</param>
/// <param name="Error">Why it did not run, or null when it did.</param>
/// <param name="Truncated">Whether output past the length limit was left out.</param>
public sealed record SoftcodeConsoleEntry(
	string Source,
	bool IsBuffer,
	bool Unsaved,
	IReadOnlyList<string> Arguments,
	IReadOnlyList<string> Output,
	string? Result,
	string? Error,
	bool Truncated);

/// <summary>
/// A console's state: its scrollback and the arguments it passes as <c>%0</c>-<c>%9</c>. Each open tab keeps
/// one, so a function attribute's runs and the arguments that exercise it stay with it.
/// </summary>
public sealed class SoftcodeConsole
{
	/// <summary>How many entries the scrollback keeps; the oldest go first.</summary>
	public const int Capacity = 100;

	/// <summary><c>%0</c> to <c>%9</c>.</summary>
	public const int MaxArguments = 10;

	private readonly List<SoftcodeConsoleEntry> _entries = [];
	private readonly List<string> _arguments = [];

	/// <summary>Oldest first.</summary>
	public IReadOnlyList<SoftcodeConsoleEntry> Entries => _entries;

	/// <summary><c>%0</c> onward, as typed.</summary>
	public IReadOnlyList<string> Arguments => _arguments;

	/// <summary>
	/// The arguments to send: through the last one with text, so an empty field at the end is no argument
	/// while an empty one before a filled one still holds its place.
	/// </summary>
	public IReadOnlyList<string> ArgumentsToSend =>
		_arguments.Take(_arguments.FindLastIndex(a => a.Length > 0) + 1).ToList();

	public void Add(SoftcodeConsoleEntry entry)
	{
		_entries.Add(entry);
		if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
	}

	public void Clear() => _entries.Clear();

	/// <summary>Adds the next argument field, up to <c>%9</c>.</summary>
	public void AddArgument()
	{
		if (_arguments.Count < MaxArguments) _arguments.Add(string.Empty);
	}

	/// <summary>Removes the last argument field.</summary>
	public void RemoveArgument()
	{
		if (_arguments.Count > 0) _arguments.RemoveAt(_arguments.Count - 1);
	}

	public void SetArgument(int index, string value)
	{
		if (index >= 0 && index < _arguments.Count) _arguments[index] = value;
	}
}
