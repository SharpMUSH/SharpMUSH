using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation;

internal class AntlrInputStreamSpan(string input, string sourceName) : ICharStream, IIntStream
{
	private readonly char[] _data = input.ToCharArray();

	private ReadOnlySpan<char> Data => _data;

	public int Index { get; private set; }

	public int Size { get; } = input.Length;

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
		var a = interval.a;
		var num = interval.b;
		if (num >= Size)
		{
			num = Size - 1;
		}

		if (a >= Size)
		{
			return string.Empty;
		}

		return Data[a..(num+1)].ToString();
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

		if (Index + i - 1 >= Size)
		{
			return -1;
		}

		return Data[Index + i - 1];
	}

	public int Mark() => -1;

	public void Release(int marker) { }

	public void Seek(int index)
	{
		if (index <= Index)
		{
			Index = index;
			return;
		}

		index = Math.Min(index, Size);
		while (Index < index)
		{
			Consume();
		}
	}
}