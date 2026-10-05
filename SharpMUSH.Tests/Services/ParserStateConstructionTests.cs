using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="ParserState"/>'s factories (<c>RootFor</c>, <c>ForTypedLine</c>, <c>ForTrackedEvaluation</c>,
/// <c>ForFunction</c>, <c>SnapshotForQueuedAction</c>) are the only places
/// that spell out the record's positional fields. A state built field by field somewhere else decides
/// for itself which registers and counters it shares with its caller, which is how evaluations ended
/// up sharing (or dropping) counters they should not have (#966).
/// </summary>
public partial class ParserStateConstructionTests
{
	[GeneratedRegex(@"\bnew\s+ParserState\s*\(")]
	private static partial Regex PositionalConstruction();

	[Test]
	public async Task OnlyParserStateItselfCallsTheConstructor()
	{
		var offenders = TestPaths.ProductionSourceFiles()
			.Concat(TestPaths.TestSourceFiles())
			.Where(path => Path.GetFileName(path) != "ParserState.cs")
			.Where(path => PositionalConstruction().IsMatch(File.ReadAllText(path)))
			.Select(path => Path.GetRelativePath(TestPaths.RepositoryRoot, path))
			.Order()
			.ToArray();

		await Assert.That(offenders).IsEmpty()
			.Because("a ParserState is built with one of its factories, plus a `with` expression for any extras");
	}
}
