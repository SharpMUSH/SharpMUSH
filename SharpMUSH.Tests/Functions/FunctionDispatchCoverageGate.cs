namespace SharpMUSH.Tests.Functions;

/// <summary>
/// After a whole-suite run, every registered function has been dispatched by the parser at least
/// once, or is listed in <see cref="KnownGaps"/> with the reason it cannot be yet (#974).
///
/// <para><see cref="RegistryCoverageInventoryTests"/> only proves that a name appears in a test
/// source, which an error-message string or a doc comment satisfies. Twelve functions passed that
/// check without ever running, and running them against PennMUSH turned up differences in half of
/// them (GRA-126). This gate uses the parser's own invocation telemetry, which only exists for the
/// whole suite, so it runs as a session hook and stands aside when the run is filtered.</para>
///
/// <para>The list works both ways: a function missing from it fails the run, and so does an entry
/// that has started being dispatched, so a fix cannot leave a stale excuse behind.</para>
/// </summary>
public static class FunctionDispatchCoverageGate
{
	/// <summary>Registered functions no test dispatches yet, each with its reason.</summary>
	public static readonly Dictionary<string, string> KnownGaps = new(StringComparer.OrdinalIgnoreCase)
	{
		// FunctionArityParityTests.AttribSetSharpIsRegisteredButTheLexerCannotReachIt pins this.
		["attrib_set#"] = "the lexer's function-name token does not admit '#', so the call never lexes as one",
	};

	[After(TestSession)]
	public static void EveryRegisteredFunctionWasDispatched(TestSessionContext context)
	{
		if (!string.IsNullOrEmpty(context.TestFilter))
			return;

		var report = FunctionCoverage.Build(ServerWebAppFactory.DispatchedFunctionNames);
		var undispatched = report.MentionedOnly.Concat(report.NeverMentioned).ToHashSet(StringComparer.OrdinalIgnoreCase);

		var unexpected = undispatched.Where(name => !KnownGaps.ContainsKey(name)).Order().ToList();
		var stale = KnownGaps.Keys.Where(name => !undispatched.Contains(name)).Order().ToList();
		if (unexpected.Count == 0 && stale.Count == 0)
			return;

		throw new InvalidOperationException(
			$"Function dispatch coverage: {report.Executed.Count} of {report.Registered.Count} registered functions ran. " +
			$"Never dispatched and not in {nameof(FunctionDispatchCoverageGate)}.{nameof(KnownGaps)}: [{string.Join(", ", unexpected)}]. " +
			$"Listed in {nameof(KnownGaps)} but now dispatched, so remove them: [{string.Join(", ", stale)}].");
	}
}
