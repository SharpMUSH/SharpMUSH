namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: SharpConfig(Name = "null_eq_zero", Description = "Treat null values as zero in numeric operations")] bool NullEqualsZero,
	[property: SharpConfig(Name = "tiny_booleans", Description = "Use TinyMUSH-style boolean evaluation (0/1 instead of true/false)")] bool TinyBooleans,
	[property: SharpConfig(Name = "tiny_trim_fun", Description = "Use TinyMUSH-style string trimming in functions")] bool TinyTrimFun,
	[property: SharpConfig(Name = "tiny_math", Description = "Use TinyMUSH-style math operations and precedence")] bool TinyMath,
	[property: SharpConfig(Name = "silent_pemit", Description = "Suppress permission error messages for pemit command")] bool SilentPEmit
);