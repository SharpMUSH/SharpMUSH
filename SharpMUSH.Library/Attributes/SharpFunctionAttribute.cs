using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SharpFunctionAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;

	/// <summary>
	/// The most arguments a call may carry. PennMUSH's <c>flist[]</c> spells "no limit" as
	/// <c>INT_MAX</c> and so does this default, because an omitted maximum has to fail open: a
	/// number here is a refusal, and refusing a call nobody meant to bound is invisible until
	/// somebody writes the call. A function that really is bounded declares its own (#1102).
	/// </summary>
	public int MaxArgs { get; set; } = int.MaxValue;
	public required FunctionFlags Flags { get; set; } = FunctionFlags.Regular;
	/// <summary>With HasSideFX, gate only calls with at least this many arguments. Zero gates every call.</summary>
	public int SideEffectMinArgs { get; set; }
	public string[] Restrict { get; set; } = [];
	/// <summary>
	/// Optional parameter names for better IDE support (inlay hints, signature help, etc.)
	/// Names should match the help file documentation (without angle brackets).
	/// Special patterns supported:
	/// - "param..." for variadic parameters (generates param1, param2, etc.)
	/// - "case...|result..." for paired repeating parameters
	/// If not provided, generic names like "arg1", "arg2" will be used.
	/// </summary>
	public string[] ParameterNames { get; set; } = [];
}