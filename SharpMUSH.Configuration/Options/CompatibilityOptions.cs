namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: PennConfig(Name = "null_eq_zero", Description = "Treat null values as zero in numeric operations")] bool NullEqualsZero,
	[property: PennConfig(Name = "tiny_booleans", Description = "Use TinyMUSH-style boolean evaluation (0/1 instead of true/false)")] bool TinyBooleans,
	[property: PennConfig(Name = "tiny_trim_fun", Description = "Use TinyMUSH-style string trimming in functions")] bool TinyTrimFun,
	[property: PennConfig(Name = "tiny_math", Description = "Use TinyMUSH-style math operations and precedence")] bool TinyMath,
	[property: PennConfig(Name = "silent_pemit", Description = "Suppress permission error messages for pemit command")] bool SilentPEmit
);