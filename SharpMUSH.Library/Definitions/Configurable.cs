using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Definitions;

public static class Configurable
{
	private const uint MaxFloatPrecision = 15;

	/// <summary>
	/// The precision answered before a host calls <see cref="ReadFloatPrecisionFrom"/> — in a tool, a
	/// test that boots no server, or any read before startup has wired the live configuration.
	/// </summary>
	/// <remarks>
	/// Read from <see cref="Services.OptionsService.Default"/> rather than written down again, the
	/// same way the alias tables below dereference <see cref="AliasOptions.Default"/>. It was a
	/// separate literal and it had drifted: 15 here and in two other places, where PennMUSH and the
	/// <c>mushcnf.dst</c> this repository ships both say 6 (#1194). Evaluated once, because
	/// <see cref="FloatPrecision"/> is read for every number the server formats.
	/// </remarks>
	public static readonly uint DefaultFloatPrecision = Services.OptionsService.Default().Cosmetic.FloatPrecision;

	private static Func<uint> _floatPrecision = () => DefaultFloatPrecision;

	/// <summary>
	/// Decimal places in floating-point output, as <c>float_precision</c> sets it: read on every call,
	/// so a configuration change applies to the next result. At most 15, and 6 until a host wires it.
	/// See <see cref="Utilities.MushNumber"/>.
	/// </summary>
	public static int FloatPrecision => (int)Math.Min(_floatPrecision(), MaxFloatPrecision);

	/// <summary>Reads <see cref="FloatPrecision"/> from the live configuration; called once at startup.</summary>
	public static void ReadFloatPrecisionFrom(Func<uint> source) => _floatPrecision = source;
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