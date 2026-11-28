using Antlr4.Runtime;
using SharpMUSH.Library.ParserInterfaces;
using Antlr4.Runtime.Misc;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation;

internal sealed class StringSpanInputStream(string input, string sourceName) : ICharStream
{
	private readonly string _input = input ?? string.Empty;
	internal string Input => _input;

	public int Index { get; private set; }

	public int Size => _input.Length;

	public string SourceName => sourceName;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Consume()
	{
		if ((Index & 1023) == 0) ExecutionBudget.Current?.ThrowIfExceeded();
		Index++;
	}

	[return: NotNull]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public string GetText(Interval interval)
	{
		var start = interval.a;
		var stop = interval.b;
		if (stop >= Size)
		{
			stop = Size - 1;
		}

		var count = stop - start + 1;
		if (start >= Size || count <= 0)
		{
			return string.Empty;
		}

		return _input.Substring(start, count);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int LA(int i)
	{
		if (i == 0)
		{
			return 0;
		}

		if (i < 0)
		{
			i++;
			if (Index + i - 1 < 0)
			{
				return -1;
			}
		}

		var position = Index + i - 1;
		if (position >= Size)
		{
			return -1;
		}

		return _input[position];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Mark() => -1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Release(int marker) { }

	/// <summary>
	/// The offset each line starts at, the first always 0, built on the first <see cref="PositionOf"/>. Trees of this
	/// input are read by many evaluations at once; two that build it together build the same table.
	/// </summary>
	private int[]? _lineStarts;

	/// <summary>
	/// The line (from 1) and column (from 0) of the character at <paramref name="index"/>, as ANTLR's lexer counts
	/// them: a line ends after each <c>\n</c>, and a column counts UTF-16 code units.
	/// </summary>
	internal (int Line, int Column) PositionOf(int index)
	{
		var starts = _lineStarts ??= LineStarts(_input);
		var line = Array.BinarySearch(starts, index);
		if (line < 0)
		{
			line = Math.Max(~line - 1, 0);
		}

		return (line + 1, index - starts[line]);
	}

	private static int[] LineStarts(string text)
	{
		var starts = new List<int> { 0 };
		for (var at = text.IndexOf('\n'); at >= 0; at = text.IndexOf('\n', at + 1))
		{
			starts.Add(at + 1);
		}

		return [.. starts];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Seek(int index)
	{
		if (index <= Index)
		{
			Index = index;
			return;
		}

		Index = Math.Min(index, Size);
	}
}
