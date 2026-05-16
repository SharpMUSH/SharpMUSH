using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation;

internal sealed class StringSpanInputStream(string input, string sourceName) : ICharStream
{
	private readonly string _input = input ?? string.Empty;

	public int Index { get; private set; }

	public int Size => _input.Length;

	public string SourceName => sourceName;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Consume()
	{
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
