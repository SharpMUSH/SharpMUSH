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
	/// <summary>
	/// The alias names PennMUSH ships, as the shipped helpfiles document them: every entry here has a
	/// topic that says in so many words that it is another name for the function it maps to. It is the
	/// one copy — <c>OptionsService</c> seeds the database from it rather than repeating it, because
	/// the two copies had already diverged into eight aliases that were documented and unregistered.
	/// </summary>
	public static Dictionary<string, string[]> DefaultFunctionAliases => new()
	{
		{ "atrlock", ["attrlock"] },
		{ "e", ["exp"] },
		{ "flip", ["reverse"] },
		{ "host", ["hostname"] },
		{ "iter", ["parse"] },
		{ "lreplace", ["replace"] },
		{ "lsearch", ["search"] },
		{ "lstats", ["stats"] },
		{ "lthings", ["lobjects"] },
		{ "lvthings", ["lvobjects"] },
		{ "match", ["element"] },
		{ "mean", ["avg"] },
		{ "modulo", ["mod", "modulus"] },
		{ "moniker", ["cname"] },
		{ "nattr", ["attrcnt"] },
		{ "nattrp", ["attrpcnt"] },
		{ "nthings", ["nobjects"] },
		{ "nvthings", ["nvobjects"] },
		{ "randword", ["pickrand"] },
		{ "soundslike", ["soundlike"] },
		{ "speak", ["speakpenn"] },
		{ "textfile", ["dynhelp"] },
		{ "trunc", ["val"] },
		{ "ufun", ["u"] },
		{ "xthings", ["xobjects"] },
		{ "xvthings", ["xvobjects"] }
	};

	/// <inheritdoc cref="DefaultFunctionAliases"/>
	public static Dictionary<string, string[]> DefaultCommandAliases => new()
	{
		{ "@ATRLOCK", ["@attrlock"] },
		{ "@ATRCHOWN", ["@attrchown"] },
		{ "@EDIT", ["@gedit"] },
		{ "@IFELSE", ["@if"] },
		{ "@SWITCH", ["@sw"] },
		{ "GET", ["take"] },
		{ "GOTO", ["move"] },
		{ "INVENTORY", ["i"] },
		{ "LOOK", ["l"] },
		{ "PAGE", ["p"] },
		{ "WHISPER", ["w"] }
	};

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