using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Components;

/// <summary>
/// What one <see cref="GlobalTerminal"/> shows: the service's lines when it mounted, the lines that
/// arrived since and the terminal's own notices, bounded like the service's buffer.
/// </summary>
/// <remarks>
/// Kept apart from the service's buffer on purpose: clearing the screen clears this view only, and a
/// notice written here (an error, a diagnostic) is this terminal's, not a line the server sent.
/// </remarks>
public sealed class TerminalTranscript
{
	private readonly List<TerminalLine> _lines;

	public TerminalTranscript(IEnumerable<TerminalLine> initial)
	{
		_lines = [.. initial];
		Trim();
	}

	public IReadOnlyList<TerminalLine> Lines => _lines;

	/// <summary>Appends <paramref name="line"/>, dropping the oldest once the transcript is full.</summary>
	public void Add(TerminalLine line)
	{
		_lines.Add(line);
		Trim();
	}

	public void Clear() => _lines.Clear();

	private void Trim()
	{
		if (_lines.Count > TerminalService.MaxLines)
		{
			_lines.RemoveRange(0, _lines.Count - TerminalService.MaxLines);
		}
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
