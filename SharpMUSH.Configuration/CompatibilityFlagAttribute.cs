namespace SharpMUSH.Configuration;

/// <summary>A server whose behaviour a compatibility option emulates, or whose database import sets one.</summary>
[Flags]
public enum CompatibilityServer
{
	None = 0,
	PennMUSH = 1,
	TinyMUSH = 2,
	TinyMUX = 4,
	RhostMUSH = 8
}

/// <summary>
/// Marks a <c>Compatibility</c> option as a feature flag: which servers it emulates, which database imports
/// turn it on and where it is documented. It is the one place those facts are written: the importers read
/// <see cref="SetBy"/> (<see cref="CompatibilityFlags.ImportOptions"/>), the configuration metadata adds the
/// sentence naming the imports to the option's description, and <c>@config compatibility</c> lists all three.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CompatibilityFlagAttribute : Attribute
{
	/// <summary>The servers whose behaviour the option gives. The option's <c>Group</c> names the first of them, its origin.</summary>
	public required CompatibilityServer Emulates { get; set; }

	/// <summary>
	/// The database imports that turn the option on. A boolean option is set true; <c>dialect</c> is set to the
	/// importing server's dialect.
	/// </summary>
	public CompatibilityServer SetBy { get; set; }

	/// <summary>The help lookup of the <c>compatibility config</c> section that documents the option.</summary>
	public required string HelpTopic { get; set; }
}
