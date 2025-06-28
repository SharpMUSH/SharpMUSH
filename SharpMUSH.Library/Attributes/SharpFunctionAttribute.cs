using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SharpFunctionAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public required FunctionFlags Flags { get; set; } = FunctionFlags.Regular;
}