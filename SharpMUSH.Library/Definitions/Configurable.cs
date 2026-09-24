using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Definitions;

public static class Configurable
{
	private const uint MaxFloatPrecision = 15;

	/// <summary>
	/// The precision answered outside an engine's evaluation: in a tool, a test that boots no server,
	/// or any number written by code no parser is running.
	/// </summary>
	/// <remarks>
	/// Read from <see cref="Services.OptionsService.Default"/> rather than written down again, the
	/// same way the alias tables below dereference <see cref="AliasOptions.Default"/>. It was a
	/// separate literal and it had drifted: 15 here and in two other places, where PennMUSH and the
	/// <c>mushcnf.dst</c> this repository ships both say 6 (#1194). Evaluated once, because
	/// <see cref="FloatPrecision"/> is read for every number the server formats.
	/// </remarks>
	public static readonly uint DefaultFloatPrecision = Services.OptionsService.Default().Cosmetic.FloatPrecision;

	private static readonly Func<uint> DefaultFloatPrecisionSource = () => DefaultFloatPrecision;

	private static readonly AsyncLocal<Func<uint>?> EngineFloatPrecision = new();

	/// <summary>
	/// Decimal places in floating-point output, as <c>float_precision</c> sets it: read on every call,
	/// so a configuration change applies to the next result. At most 15. Inside an evaluation it is the
	/// evaluating engine's setting (see <see cref="UseFloatPrecisionOf"/>); anywhere else it is
	/// <see cref="DefaultFloatPrecision"/>. See <see cref="Utilities.MushNumber"/>.
	/// </summary>
	public static int FloatPrecision => (int)Math.Min((EngineFloatPrecision.Value ?? DefaultFloatPrecisionSource)(), MaxFloatPrecision);

	/// <summary>
	/// Makes <paramref name="source"/> the <see cref="FloatPrecision"/> of this async flow until the
	/// returned scope is disposed. The parser enters it for every evaluation, with its own options.
	/// </summary>
	/// <remarks>
	/// Scoped to the flow rather than set once for the process: that was a static each host re-pointed
	/// at its own options as it started, so in a process running more than one engine (the test run
	/// does) every number was written at the precision of whichever host started last (#1245).
	/// Answers <see langword="null"/> when the flow already reads <paramref name="source"/>, which is
	/// every nested evaluation, so those cost nothing.
	/// </remarks>
	public static IDisposable? UseFloatPrecisionOf(Func<uint> source)
	{
		var previous = EngineFloatPrecision.Value;
		if (ReferenceEquals(previous, source)) return null;
		EngineFloatPrecision.Value = source;
		return new FloatPrecisionScope(previous);
	}

	private sealed class FloatPrecisionScope(Func<uint>? previous) : IDisposable
	{
		public void Dispose() => EngineFloatPrecision.Value = previous;
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