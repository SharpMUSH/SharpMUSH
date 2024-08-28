using Antlr4.Runtime;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation;

internal class AntlrInputStreamSpan : BaseInputCharStream
{
	private readonly char[] _data;
	private ReadOnlySpan<char> Data => _data;

	public AntlrInputStreamSpan(string data)
	{
		_data = data.ToCharArray();
		n = data.Length;
	}

	protected override string ConvertDataToString(int start, int count) => Data.Slice(start, count).ToString();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected override int ValueAt(int i) => Data[i];
}