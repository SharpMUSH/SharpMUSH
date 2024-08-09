using Antlr4.Runtime;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Implementation;

internal class AntlrInputMarkupStringStream(MString data) : BaseInputCharStream
{
	protected override string ConvertDataToString(int start, int count) => MModule.substring(start, count, data).ToString();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected override int ValueAt(int i) => 
		MModule.substring(i, 1, data).ToString()[0];
}