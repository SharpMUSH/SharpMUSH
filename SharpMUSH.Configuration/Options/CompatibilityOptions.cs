namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[PennConfig(Name = "null_eq_zero")] bool NullEqualsZero,
	[PennConfig(Name = "tiny_booleans")]bool TinyBooleans,
	[PennConfig(Name = "tiny_trim_fun")]bool TinyTrimFun,
	[PennConfig(Name = "tiny_math")]bool TinyMath,
	[PennConfig(Name = "silent_pemit")]bool SilentPEmit
);