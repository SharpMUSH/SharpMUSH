namespace SharpMUSH.Configuration.Options;

public record SharpMUSHOptions
{
	public required AttributeOptions Attribute { get; init; }
	public required ChatOptions Chat { get; init; }
	public required CommandOptions Command { get; init; }
	public required CompatibilityOptions Compatibility { get; init; }
	public required CosmeticOptions Cosmetic { get; init; }
	public required CostOptions Cost { get; init; }
	public required DatabaseOptions Database { get; init; }
	public required DumpOptions Dump { get; init; }
	public required FileOptions File { get; init; }
	public required FlagOptions Flag { get; init; }
	public required FunctionOptions Function { get; init; }
	public required LimitOptions Limit { get; init; }
	public required LogOptions Log { get; init; }
	public required MessageOptions Message { get; init; }
	public required NetOptions Net { get; init; }
	public required DebugOptions Debug { get; init; }
	public required AliasOptions Alias { get; init; }
	public required RestrictionOptions Restriction { get; init; }
};