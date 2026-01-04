using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SharpCommandAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public string CommandLock { get; set; } = string.Empty;
	public CommandBehavior Behavior { get; set; } = CommandBehavior.Default;
	public string[]? Switches { get; set; } = [];
	/// <summary>
	/// Optional parameter names for better IDE support (inlay hints, signature help, etc.)
	/// Names should match the help file documentation.
	/// Special patterns supported:
	/// - "param..." for variadic parameters (generates param1, param2, etc.)
	/// - "case...|result..." for paired repeating parameters
	/// </summary>
	public string[] ParameterNames { get; set; } = [];
}