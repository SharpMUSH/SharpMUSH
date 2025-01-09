namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: PennConfig(Name = "null_eq_zero")] bool NullEqualsZero,
	[property: PennConfig(Name = "tiny_booleans")]bool TinyBooleans,
	[property: PennConfig(Name = "tiny_trim_fun")]bool TinyTrimFun,
	[property: PennConfig(Name = "tiny_math")]bool TinyMath,
	[property: PennConfig(Name = "silent_pemit")]bool SilentPEmit
);