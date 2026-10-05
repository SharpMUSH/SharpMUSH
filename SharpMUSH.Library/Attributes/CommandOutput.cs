namespace SharpMUSH.Library.Attributes;

/// <summary>
/// What a command leaves in <c>%|</c>, the output of the last command run in an action list. The output
/// is the logical answer the command's function analog would give (<c>@dig</c> outputs what <c>dig()</c>
/// returns), never the text the command shows. Each command's help documents it in its
/// <c>::: output</c> block.
/// </summary>
public enum CommandOutput
{
	/// <summary>No logical output: <c>%|</c> is cleared, or holds the <c>#-1</c> error when the command failed.</summary>
	None = 0,

	/// <summary>The command's return value becomes <c>%|</c>.</summary>
	Value = 1,

	/// <summary>
	/// The command runs an action list. In place (<c>/inline</c>, <c>@include</c>) <c>%|</c> is the output of the
	/// last command that list ran; queued, nothing has run yet and <c>%|</c> is cleared as for <see cref="None"/>.
	/// </summary>
	Runs = 2,

	/// <summary>The command leaves <c>%|</c> as it found it: <c>@@</c>, <c>@assert</c>, <c>@break</c>, and the
	/// <c>]</c> and <c>~</c> modifiers, whose wrapped command records its own output.</summary>
	Passthrough = 3,
}
