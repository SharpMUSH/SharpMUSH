namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: SharpConfig(Name = "null_eq_zero", Category = "Compatibility", Description = "Treat null values as zero in numeric operations")] bool NullEqualsZero,
	[property: SharpConfig(Name = "tiny_booleans", Category = "Compatibility", Description = "Use TinyMUSH-style boolean evaluation (0/1 instead of true/false)")] bool TinyBooleans,
	[property: SharpConfig(Name = "tiny_trim_fun", Category = "Compatibility", Description = "Use TinyMUSH-style string trimming in functions")] bool TinyTrimFun,
	[property: SharpConfig(Name = "tiny_math", Category = "Compatibility", Description = "Use TinyMUSH-style math operations and precedence")] bool TinyMath,
	[property: SharpConfig(Name = "silent_pemit", Category = "Compatibility", Description = "Suppress permission error messages for pemit command")] bool SilentPEmit
);