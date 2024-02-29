
using AntlrCSharp.Implementation.Constants;

namespace AntlrCSharp.Implementation.Functions
{
	public class PennFunctionAttribute : Attribute
	{
		public required string Name { get; set; }
		public int MinArgs { get; set; } = 0;
		public int MaxArgs { get; set; } = 32;
		public required FunctionFlags Flags { get; set; } = FunctionFlags.Regular;
	}
}