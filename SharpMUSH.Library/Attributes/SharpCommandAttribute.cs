using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SharpCommandAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public string CommandLock { get; set; } = string.Empty;
	public CommandBehavior Behavior { get; set; } = CommandBehavior.Default;
	public string[]? Switches { get; set; } = [];
}