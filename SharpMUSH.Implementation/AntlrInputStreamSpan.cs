using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace SharpMUSH.Implementation;

internal class AntlrInputStreamSpan(string input, string sourceName) : ICharStream, IIntStream
{
	private readonly char[] _data = input.ToCharArray();

	private readonly int n = input.Length;

	private int p = 0;

	private ReadOnlySpan<char> Data => _data;

	public int Index => p;

	public int Size => n;

	public string SourceName => sourceName;

	public void Consume()
	{
		if (p >= n)
		{
			throw new InvalidOperationException("cannot consume EOF");
		}

		p++;
	}

	[return: NotNull]
	public string GetText(Interval interval)
	{
		var a = interval.a;
		var num = interval.b;
		if (num >= n)
		{
			num = n - 1;
		}

		var count = num - a + 1;
		if (a >= n)
		{
			return string.Empty;
		}

		return Data.Slice(a, count).ToString();
	}

	public int LA(int i)
	{
		if (i == 0)
		{
			return 0;
		}

		if (i < 0)
		{
			i++;
			if (p + i - 1 < 0)
			{
				return -1;
			}
		}

		if (p + i - 1 >= n)
		{
			return -1;
		}

		return Data[p + i - 1];
	}

	public int Mark()
	{
		return -1;
	}

	public void Release(int marker)
	{
	}

	public void Seek(int index)
	{
		if (index <= p)
		{
			p = index;
			return;
		}

		index = Math.Min(index, n);
		while (p < index)
		{
			Consume();
		}
	}
}