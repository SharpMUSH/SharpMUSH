namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Copies what a character is told while a command it issued from the web portal runs, so the portal
/// can show the command's own output as its answer (<c>POST api/commands</c>).
/// </summary>
/// <remarks>
/// <para>Unlike <see cref="IHttpOutputCapture"/>, this does not take the output away from anyone: the
/// character is a real player and may be connected elsewhere, and every connection of theirs still
/// hears it. The capture is a copy for the portal.</para>
///
/// <para>The active frame lives in an <see cref="AsyncLocal{T}"/>, so it covers exactly the work that
/// runs in the command's own queue entry — the command, and a <c>$</c>-command it matches, which runs in
/// place for a typed line. Work the command queues (<c>@wait</c>, <c>@trigger</c>) runs in an entry of
/// its own and is not copied, as it is not part of the command's answer in PennMUSH either.</para>
/// </remarks>
public interface ICommandOutputCapture
{
	/// <summary>
	/// Begins copying output sent to <paramref name="character"/> into <paramref name="transcript"/>.
	/// Dispose the returned scope to stop (restores any previously active frame).
	/// </summary>
	IDisposable BeginCapture(int character, CommandTranscript transcript);

	/// <summary>
	/// Offers a piece of output to the active capture frame. Copied when it is addressed to the
	/// captured character; delivery to that character's connections is unaffected either way.
	/// </summary>
	void Offer(int dbref, string text);
}

/// <summary>The lines a captured command produced for its character, bounded in total length.</summary>
public sealed class CommandTranscript(int maxLength)
{
	private readonly List<string> _lines = [];
	private int _length;

	/// <summary>Every line copied so far, in the order it was sent.</summary>
	public IReadOnlyList<string> Lines
	{
		get
		{
			lock (_lines) return [.. _lines];
		}
	}

	/// <summary>True once a line was dropped because the transcript reached its length limit.</summary>
	public bool Truncated { get; private set; }

	/// <summary>Appends <paramref name="line"/>, or marks the transcript truncated when it would not fit.</summary>
	public void Append(string line)
	{
		lock (_lines)
		{
			if (Truncated || _length + line.Length > maxLength)
			{
				Truncated = true;
				return;
			}

			_lines.Add(line);
			_length += line.Length;
		}
	}
}
