namespace SharpMUSH.Configuration.Options;

public record PennMUSHOptions(
	AttributeOptions Attribute,
	ChatOptions Chat,
	CommandOptions Command,
	CompatibilityOptions Compatibility,
	CosmeticOptions Cosmetic,
	CostOptions Cost,
	DatabaseOptions Database,
	DumpOptions Dump,
	FileOptions File,
	FlagOptions Flag,
	FunctionOptions Function,
	LimitOptions Limit,
	LogOptions Log,
	MessageOptions Message,
	NetConfig Net
);