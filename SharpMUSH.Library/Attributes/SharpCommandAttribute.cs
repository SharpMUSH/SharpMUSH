using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SharpCommandAttribute : Attribute
{
	public required string Name { get; set; }
	public int MinArgs { get; set; } = 0;
	public int MaxArgs { get; set; } = 32;
	public string CommandLock { get; set; } = string.Empty;

	/// <summary>
	/// What a refusal says in place of "Permission denied.", as <c>@command/restrict</c> sets it from
	/// the text after the restriction's first <c>"</c>. Penn keeps it on the COMMAND_INFO
	/// (<c>src/command.h:161</c>); the attribute instance is per-<c>Commands</c>-instance, so this is
	/// mutable for the same reason <see cref="CommandLock"/> is.
	/// </summary>
	public string RestrictMessage { get; set; } = string.Empty;
	public CommandBehavior Behavior { get; set; } = CommandBehavior.Default;
	public string[]? Switches { get; set; } = [];
	/// <summary>Switches that treat the complete argument text as one argument, retaining normal evaluation policy.</summary>
	public string[] SingleArgumentSwitches { get; set; } = [];
	/// <summary>
	/// Optional parameter names for better IDE support (inlay hints, signature help, etc.)
	/// Names should match the help file documentation.
	/// Special patterns supported:
	/// - "param..." for variadic parameters (generates param1, param2, etc.)
	/// - "case...|result..." for paired repeating parameters
	/// </summary>
	public string[] ParameterNames { get; set; } = [];

	/// <summary>
	/// Names <c>@command/args</c> gives the command's arguments, in order (the left side, then each
	/// right-side argument): its hooks read each as <c>%&lt;name&gt;</c> as well as <c>LSA&lt;n&gt;</c>.
	/// Mutable for the same reason <see cref="CommandLock"/> is.
	/// </summary>
	public string[] ArgumentNames { get; set; } = [];

	/// <summary>What the command leaves in <c>%></c>. See <see cref="CommandOutput"/>.</summary>
	public CommandOutput Output { get; set; } = CommandOutput.None;
}