using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Definitions;

public static class Configurable
{
	private const int MinFloatPrecision = 0;
	private const int MaxFloatPrecision = 15;
	private const int DefaultFloatPrecision = 15;
	private static int _floatPrecision = DefaultFloatPrecision;

	/// <summary>
	/// Number of significant digits for floating-point output.
	/// Clamped to [0, 15]. PennMUSH default: 6, SharpMUSH default: 15.
	/// Set via float_precision config option (Cosmetic category).
	/// </summary>
	public static int FloatPrecision
	{
		get => _floatPrecision;
		set => _floatPrecision = Math.Clamp(value, MinFloatPrecision, MaxFloatPrecision);
	}
	/// <inheritdoc cref="AliasOptions.Default"/>
	public static Dictionary<string, string[]> DefaultFunctionAliases => AliasOptions.Default.FunctionAliases;

	/// <inheritdoc cref="AliasOptions.Default"/>
	public static Dictionary<string, string[]> DefaultCommandAliases => AliasOptions.Default.CommandAliases;

	public static Dictionary<string, string[]> FunctionAliases { get; private set; } = DefaultFunctionAliases;

	public static Dictionary<string, string[]> CommandAliases { get; private set; } = DefaultCommandAliases;

	public static Dictionary<string, string[]> CommandRestrictions { get; private set; } = new();

	public static Dictionary<string, string[]> FunctionRestrictions { get; private set; } = new();

	/// <summary>
	/// Initialize configurable aliases and restrictions from database-backed options.
	/// This should be called once during application startup.
	/// </summary>
	/// <param name="aliasOptions">Alias options from database</param>
	/// <param name="restrictionOptions">Restriction options from database</param>
	public static void Initialize(AliasOptions aliasOptions, RestrictionOptions restrictionOptions)
	{
		FunctionAliases = aliasOptions.FunctionAliases;
		CommandAliases = aliasOptions.CommandAliases;
		CommandRestrictions = restrictionOptions.CommandRestrictions;
		FunctionRestrictions = restrictionOptions.FunctionRestrictions;
	}
}