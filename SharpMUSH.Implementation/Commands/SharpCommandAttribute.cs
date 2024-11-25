using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Commands;

[AttributeUsage(AttributeTargets.Method)]
public class SharpCommandAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public string CommandLock { get; set; } = string.Empty;
	public CommandBehavior Behavior { get; set; }
	public string[]? Switches { get; set; }
}