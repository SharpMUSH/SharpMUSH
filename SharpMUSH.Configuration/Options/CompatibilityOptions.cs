namespace SharpMUSH.Configuration.Options;

public record CompatibilityOptions(
	[property: SharpConfig(
		Name = "null_eq_zero",
		Category = "Compatibility",
		Description = "Treat null values as zero in numeric operations",
		Group = "Numeric Behavior",
		Order = 1)]
	bool NullEqualsZero,

	[property: SharpConfig(
		Name = "tiny_booleans",
		Category = "Compatibility",
		Description = "Only values with a nonzero leading signed integer are true (TinyMUSH booleans)",
		Group = "TinyMUSH Compatibility",
		Order = 1)]
	bool TinyBooleans,

	[property: SharpConfig(
		Name = "tiny_trim_fun",
		Category = "Compatibility",
		Description = "Use TinyMUSH-style string trimming in functions",
		Group = "TinyMUSH Compatibility",
		Order = 2)]
	bool TinyTrimFun,

	[property: SharpConfig(
		Name = "tiny_math",
		Category = "Compatibility",
		Description = "Treat nonnumeric strings as zero in numeric operations (TinyMUSH math)",
		Group = "TinyMUSH Compatibility",
		Order = 3)]
	bool TinyMath,

	[property: SharpConfig(
		Name = "silent_pemit",
		Category = "Compatibility",
		Description = "Suppress permission error messages for pemit command",
		Group = "Command Behavior",
		Order = 1)]
	bool SilentPEmit
);
