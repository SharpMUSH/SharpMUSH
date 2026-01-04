using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SharpFunctionAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public required FunctionFlags Flags { get; set; } = FunctionFlags.Regular;
	public string[] Restrict { get; set; } = [];
	/// <summary>
	/// Optional parameter names for better IDE support (inlay hints, signature help, etc.)
	/// Names should match the help file documentation (without angle brackets).
	/// If not provided, generic names like "arg1", "arg2" will be used.
	/// </summary>
	public string[] ParameterNames { get; set; } = [];
}