namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: PennConfig(Name = "null_eq_zero", Description = "Treat null values as zero in numeric operations", DefaultValue = "yes")] bool NullEqualsZero,
	[property: PennConfig(Name = "tiny_booleans", Description = "Use TinyMUSH-style boolean evaluation (0/1 instead of true/false)", DefaultValue = "no")] bool TinyBooleans,
	[property: PennConfig(Name = "tiny_trim_fun", Description = "Use TinyMUSH-style string trimming in functions", DefaultValue = "no")] bool TinyTrimFun,
	[property: PennConfig(Name = "tiny_math", Description = "Use TinyMUSH-style math operations and precedence", DefaultValue = "no")] bool TinyMath,
	[property: PennConfig(Name = "silent_pemit", Description = "Suppress permission error messages for pemit command", DefaultValue = "no")] bool SilentPEmit
);