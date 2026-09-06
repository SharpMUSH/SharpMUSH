using System.Buffers;
using System.Globalization;
namespace MarkupString.Ansi;

/// <summary>
/// Accumulates the <c>;</c>-separated numbers of one SGR sequence into a caller-provided span, so
/// building a sequence costs no allocation. The buffer is normally a <c>stackalloc</c>; 80 chars
/// holds the widest sequence this package produces (every attribute plus two 24-bit colours).
/// </summary>
public ref struct SgrBuilder
{
	private const string BufferTooSmall = "The SGR buffer is too small for the codes written to it.";

	private readonly Span<char> _buffer;
	private int _written;

	/// <summary>Starts an empty sequence writing into <paramref name="buffer"/>.</summary>
	public SgrBuilder(Span<char> buffer)
	{
		_buffer = buffer;
		_written = 0;
	}

	/// <summary>Whether no code has been added yet.</summary>
	public readonly bool IsEmpty => _written == 0;

	/// <summary>The codes written so far, without the <c>ESC[</c> prefix or the <c>m</c> suffix.</summary>
	public readonly ReadOnlySpan<char> Written => _buffer[.._written];

	/// <summary>Appends one parameter, preceded by a <c>;</c> when it is not the first.</summary>
	/// <exception cref="InvalidOperationException">The buffer cannot hold the code.</exception>
	public void Add(int code)
	{
		var start = _written;

		if (start > 0)
		{
			if (_buffer.Length == start) throw new InvalidOperationException(BufferTooSmall);
			_buffer[_written++] = ';';
		}

		if (!code.TryFormat(_buffer[_written..], out var length, provider: CultureInfo.InvariantCulture))
		{
			_written = start;
			throw new InvalidOperationException(BufferTooSmall);
		}

		_written += length;
	}
}

/// <summary>
/// The SGR state machine: given the style a terminal is already in and the style a run wants, it
/// writes the shortest escape sequence that gets there.
/// </summary>
/// <remarks>
/// Attributes are only ever added, never switched off individually — the 22–29 turn-off codes are
/// unreliable across MU* clients, so when anything has to go the whole state is reset with
/// <c>ESC[0m</c> and the wanted style is written out again. A bright standard foreground is emitted
/// as bold plus its base colour (<c>1;3n</c>) rather than <c>9n</c>, which is what PennMUSH does and
/// what every client understands; because of that, "bright foreground" counts as bold when
/// deciding what changed, so the bold parameter is never written twice and losing the brightness
/// correctly forces a reset.
/// </remarks>
public static class SgrWriter
{
	private const string Csi = "\u001b[";
	private const string ResetSequence = "\u001b[0m";

	/// <summary>The widest sequence this writer builds: eight attributes and two 24-bit colours.</summary>
	private const int MaxCodeLength = 80;

	/// <summary>Writes <c>ESC[0m</c>.</summary>
	public static void Reset(IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		output.Write(ResetSequence);
	}

	/// <summary>
	/// Writes the minimal SGR moving a terminal from <paramref name="from"/> to
	/// <paramref name="to"/>. Returns whether anything was written.
	/// </summary>
	public static bool Transition(in AnsiStyle from, in AnsiStyle to, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);

		if (to.Clear || Removes(from, to))
		{
			Reset(output);
			WriteDifference(AnsiStyle.None, to, output);
			return true;
		}

		return WriteDifference(from, to, output);
	}

	/// <summary>
	/// Writes the parameters for one colour: <c>39</c>/<c>49</c> for the terminal default,
	/// <c>3n</c>/<c>4n</c> (and <c>1;3n</c>/<c>10n</c> when bright) for the standard palette,
	/// <c>38;5;n</c>/<c>48;5;n</c> for an xterm index and <c>38;2;r;g;b</c>/<c>48;2;r;g;b</c> for
	/// 24-bit colour.
	/// </summary>
	public static void WriteColorCodes(AnsiColor color, bool background, ref SgrBuilder codes)
	{
		ArgumentNullException.ThrowIfNull(color);
		AppendColor(color, background, boldAlreadyWritten: false, ref codes);
	}

	/// <summary>
	/// Whether moving to <paramref name="to"/> would have to switch something off — an attribute
	/// that was on, a colour that was set, or the bold a bright foreground implies.
	/// </summary>
	private static bool Removes(in AnsiStyle from, in AnsiStyle to) =>
		(IsBold(from) && !IsBold(to))
		|| (from.Faint && !to.Faint)
		|| (from.Italic && !to.Italic)
		|| (from.Underlined && !to.Underlined)
		|| (from.Overlined && !to.Overlined)
		|| (from.Blink && !to.Blink)
		|| (from.Inverted && !to.Inverted)
		|| (from.StrikeThrough && !to.StrikeThrough)
		|| (from.Foreground is not null && to.Foreground is null)
		|| (from.Background is not null && to.Background is null);

	/// <summary>A bright standard foreground rides on the bold parameter, so it counts as bold.</summary>
	private static bool IsBold(in AnsiStyle style) =>
		style.Bold || style.Foreground is AnsiColor.Standard { Bright: true };

	/// <summary>
	/// Writes the attributes <paramref name="to"/> adds and the colours it changes as one sequence.
	/// Returns whether anything was written.
	/// </summary>
	private static bool WriteDifference(in AnsiStyle from, in AnsiStyle to, IBufferWriter<char> output)
	{
		Span<char> buffer = stackalloc char[MaxCodeLength];
		var codes = new SgrBuilder(buffer);

		var boldWritten = IsBold(from);
		if (IsBold(to) && !boldWritten)
		{
			codes.Add(1);
			boldWritten = true;
		}

		if (to.Faint && !from.Faint) codes.Add(2);
		if (to.Italic && !from.Italic) codes.Add(3);
		if (to.Underlined && !from.Underlined) codes.Add(4);
		if (to.Overlined && !from.Overlined) codes.Add(53);
		if (to.Blink && !from.Blink) codes.Add(5);
		if (to.Inverted && !from.Inverted) codes.Add(7);
		if (to.StrikeThrough && !from.StrikeThrough) codes.Add(9);

		// A bright foreground's bold parameter is always covered by the attribute pass above, since
		// IsBold(to) is true whenever it is set.
		if (to.Foreground is { } foreground && !foreground.Equals(from.Foreground))
			AppendColor(foreground, background: false, boldAlreadyWritten: boldWritten, ref codes);

		if (to.Background is { } background && !background.Equals(from.Background))
			AppendColor(background, background: true, boldAlreadyWritten: boldWritten, ref codes);

		if (codes.IsEmpty) return false;

		output.Write(Csi);
		output.Write(codes.Written);
		output.Write("m");
		return true;
	}

	private static void AppendColor(AnsiColor color, bool background, bool boldAlreadyWritten, ref SgrBuilder codes)
	{
		switch (color)
		{
			case AnsiColor.Default:
				codes.Add(background ? 49 : 39);
				break;

			// There is no "bright background" attribute distinct from bold, so backgrounds use the
			// 100-107 row directly.
			case AnsiColor.Standard standard when background:
				codes.Add((standard.Bright ? 100 : 40) + standard.Index);
				break;

			case AnsiColor.Standard standard:
				if (standard.Bright && !boldAlreadyWritten) codes.Add(1);
				codes.Add(30 + standard.Index);
				break;

			case AnsiColor.Xterm xterm:
				codes.Add(background ? 48 : 38);
				codes.Add(5);
				codes.Add(xterm.Index);
				break;

			case AnsiColor.Rgb rgb:
				codes.Add(background ? 48 : 38);
				codes.Add(2);
				codes.Add(rgb.R);
				codes.Add(rgb.G);
				codes.Add(rgb.B);
				break;
		}
	}
}
