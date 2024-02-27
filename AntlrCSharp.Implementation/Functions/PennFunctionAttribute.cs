
namespace AntlrCSharp.Implementation.Functions
{
	public class PennFunctionAttribute : Attribute
	{
		public string Name { get;set; }
		public int MinArgs { get; set; } = 0;
		public int MaxArgs { get; set; } = 32;
	}
}