using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Components;

/// <summary>
/// What one <see cref="GlobalTerminal"/> shows: the service's lines when it mounted, the lines that
/// arrived since and the terminal's own notices, bounded like the service's buffer, and above them any
/// earlier lines the player loaded from the kept log (<see cref="ITerminalLog"/>).
/// </summary>
/// <remarks>
/// <para>Kept apart from the service's buffer on purpose: clearing the screen clears this view only, and a
/// notice written here (an error, a diagnostic) is this terminal's, not a line the server sent.</para>
/// <para>Lines are held in <see cref="TerminalChunk"/>s of <see cref="TerminalChunk.Size"/>, and only the
/// newest chunk ever changes: a new line re-renders that chunk, not the whole scrollback, and the buffer
/// drops its oldest chunk whole once the rest still hold <see cref="TerminalService.MaxLines"/>. While
/// earlier lines are shown, a dropped chunk joins them instead, so the two never leave a gap.</para>
/// </remarks>
public sealed class TerminalTranscript
{
	/// <summary>The most earlier lines one view keeps; the oldest of them go first.</summary>
	public const int MaxEarlierLines = 10_000;

	private readonly List<TerminalChunk> _earlier = [];
	private readonly List<TerminalChunk> _live = [];
	private int _liveCount;
	private int _earlierCount;
	private int _nextId;

	public TerminalTranscript(IEnumerable<TerminalLine> initial)
	{
		foreach (var line in initial)
			Append(line);
		Trim();
	}

	/// <summary>Earlier lines loaded from the kept log, oldest first, above <see cref="Live"/>.</summary>
	public IReadOnlyList<TerminalChunk> Earlier => _earlier;

	/// <summary>The terminal's own lines, oldest first.</summary>
	public IReadOnlyList<TerminalChunk> Live => _live;

	/// <summary>Every line shown, oldest first.</summary>
	public IEnumerable<TerminalLine> Lines => _earlier.Concat(_live).SelectMany(c => c.Lines);

	/// <summary>The oldest line shown, earlier lines included.</summary>
	public TerminalLine? Oldest => _earlier.Count > 0 ? _earlier[0].Lines[0] : _live.Count > 0 ? _live[0].Lines[0] : null;

	/// <summary>Room for more earlier lines.</summary>
	public bool CanTakeEarlier => _earlierCount < MaxEarlierLines;

	/// <summary>Appends <paramref name="line"/>, dropping the oldest chunk once the transcript is full.</summary>
	public void Add(TerminalLine line)
	{
		Append(line);
		Trim();
	}

	/// <summary>Puts <paramref name="lines"/> (oldest first, all older than anything shown) above the rest.</summary>
	public void Prepend(IReadOnlyList<TerminalLine> lines)
	{
		var chunks = new List<TerminalChunk>();
		for (var at = 0; at < lines.Count; at += TerminalChunk.Size)
			chunks.Add(new TerminalChunk(_nextId++, lines.Skip(at).Take(TerminalChunk.Size)));
		_earlier.InsertRange(0, chunks);
		_earlierCount += lines.Count;
	}

	public void Clear()
	{
		_earlier.Clear();
		_live.Clear();
		_earlierCount = _liveCount = 0;
	}

	private void Append(TerminalLine line)
	{
		if (_live.Count == 0 || _live[^1].Full)
			_live.Add(new TerminalChunk(_nextId++, []));
		_live[^1].Add(line);
		_liveCount++;
	}

	private void Trim()
	{
		while (_live.Count > 1 && _liveCount - _live[0].Lines.Count >= TerminalService.MaxLines)
		{
			var oldest = _live[0];
			_live.RemoveAt(0);
			_liveCount -= oldest.Lines.Count;
			if (_earlier.Count == 0) continue;
			_earlier.Add(oldest);
			_earlierCount += oldest.Lines.Count;
		}
		while (_earlierCount > MaxEarlierLines && _earlier.Count > 0)
		{
			_earlierCount -= _earlier[0].Lines.Count;
			_earlier.RemoveAt(0);
		}
	}
}

/// <summary>
/// Up to <see cref="Size"/> consecutive lines of a <see cref="TerminalTranscript"/>, rendered as one unit.
/// <see cref="Version"/> moves when a line is added, which is how its view knows to render again.
/// </summary>
public sealed class TerminalChunk(int id, IEnumerable<TerminalLine> lines)
{
	public const int Size = 100;

	private readonly List<TerminalLine> _lines = [.. lines];

	/// <summary>Unique within its transcript, for the view's <c>@key</c>.</summary>
	public int Id { get; } = id;

	public int Version { get; private set; }

	public IReadOnlyList<TerminalLine> Lines => _lines;

	public bool Full => _lines.Count >= Size;

	internal void Add(TerminalLine line)
	{
		_lines.Add(line);
		Version++;
	}
}

/// <summary>What a terminal reads from, and writes to, the connection beyond the lines themselves.</summary>
public static class TerminalInput
{
	/// <summary>
	/// The player a typed <c>connect &lt;name&gt; ...</c> logs in as, so the terminal can show who it is
	/// connected as. <c>connect token ...</c> names no player.
	/// </summary>
	public static Found<string> TypedConnectName(string input)
	{
		var trimmed = input.Trim();
		if (!trimmed.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
		{
			return new NotFound();
		}

		var parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 2 && !string.Equals(parts[1], "token", StringComparison.OrdinalIgnoreCase)
			? parts[1]
			: new NotFound();
	}

	/// <summary>The name the portal's terminal gives itself, the first of its terminal types (telnet TTYPE).</summary>
	public const string ClientName = "SHARPMUSH-PORTAL";

	/// <summary>
	/// The control frame reporting what the portal's terminal is, as a telnet client reports its terminal types
	/// with MTTS: its name, then what it renders (colour at every depth, UTF-8, command links), then whether a
	/// screen reader reads it. The game reads these as it reads a telnet client's, so <c>terminfo()</c> names the
	/// client and says <c>screenreader</c> while <see cref="Services.ScreenReaderMode"/> is on.
	/// </summary>
	public static string TerminalTypesFrame(bool screenReader)
	{
		string[] types = screenReader
			? [ClientName, "ANSI", "256 COLORS", "TRUECOLOR", "UTF8", "MSLP", "SCREEN_READER"]
			: [ClientName, "ANSI", "256 COLORS", "TRUECOLOR", "UTF8", "MSLP"];
		// Fixed names with nothing to escape, so written out as NawsFrame is.
		return $"{{\"type\":\"ttype\",\"types\":[{string.Join(',', types.Select(t => $"\"{t}\""))}]}}";
	}

	/// <summary>The control frame reporting the terminal's character grid (telnet NAWS).</summary>
	public static string NawsFrame(int cols, int rows) => $"{{\"type\":\"naws\",\"cols\":{cols},\"rows\":{rows}}}";
}
