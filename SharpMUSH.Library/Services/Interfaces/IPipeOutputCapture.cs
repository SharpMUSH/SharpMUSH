namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Takes what an executor is told while a piped command runs (<c>look ;| say %|</c>), so the next
/// command reads it as <c>%|</c> — TinyMUX's command piping (<c>help piping</c>).
/// </summary>
/// <remarks>
/// <para>Like <see cref="IHttpOutputCapture"/>, and unlike <see cref="ICommandOutputCapture"/>, it takes
/// the output away: piped text is passed on, not shown, as TinyMUX does.</para>
///
/// <para>The active frame lives in an <see cref="AsyncLocal{T}"/>, so it covers exactly the work the piped
/// command does in its own queue entry. Work the command queues runs later, outside the frame, and is
/// delivered as usual.</para>
/// </remarks>
public interface IPipeOutputCapture
{
	/// <summary>
	/// Begins taking output sent to <paramref name="executor"/> into <paramref name="buffer"/>.
	/// Dispose the returned scope to stop (restores any previously active frame).
	/// </summary>
	IDisposable BeginCapture(int executor, PipeBuffer buffer);

	/// <summary>
	/// Offers a piece of output to the active frame. Returns <c>true</c> when it was addressed to the
	/// piped executor, and the caller then delivers it to no one.
	/// </summary>
	bool TryCapture(int dbref, MString text);

	/// <summary>Whether <see cref="TryCapture"/> would take output addressed to <paramref name="dbref"/>.</summary>
	bool Captures(int dbref);
}

/// <summary>The output a piped command produced for its executor, line by line and bounded in total length.</summary>
public sealed class PipeBuffer(int maxLength)
{
	private readonly List<MString> _lines = [];
	private int _length;

	/// <summary>
	/// Appends <paramref name="line"/>, which may hold several lines. When it would take the buffer past its
	/// limit, the lines of it that fit are kept and the rest, with everything after it, is dropped, so the
	/// text never ends partway through a line.
	/// </summary>
	public void Append(MString line)
	{
		lock (_lines)
		{
			if (Truncated) return;

			var separator = _lines.Count > 0 ? 1 : 0;
			var room = maxLength - _length - separator;
			if (line.Length <= room)
			{
				_lines.Add(line);
				_length += separator + line.Length;
				return;
			}

			Truncated = true;
			var cut = room >= 0 ? line.Text.LastIndexOf('\n', Math.Min(room, line.Length - 1)) : -1;
			if (cut > 0)
			{
				_lines.Add(line.Substring(0, cut));
				_length += separator + cut;
			}
		}
	}

	/// <summary>True once a line was dropped because the buffer was full.</summary>
	public bool Truncated { get; private set; }

	/// <summary>Every line taken, joined with line breaks, markup kept.</summary>
	public MString Text
	{
		get
		{
			lock (_lines)
			{
				return _lines.Count switch
				{
					0 => MarkupText.Empty,
					1 => _lines[0],
					_ => MarkupText.Join(MarkupText.Plain("\n"), _lines)
				};
			}
		}
	}
}
