using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class AnalysisProjectionTests
{
	[Test]
	public async Task CatalogProjection_ContainsOnlyVisibleNamesAndAliasesAndCannotEvaluate()
	{
		var entry = new AnalysisCatalogEntry("visible", "function", "user-defined", 1, null, ["alias"], ["value"], [], "Regular", []);
		var local = new AnalysisCatalogEntry("local", "function", "owner-local", 0, 0, [], [], [], "Regular", []);
		var analyzer = AnalysisCatalogAnalyzer.Create(new AnalysisCatalogSnapshot("epoch", "revision", "live", [entry, local]), SharpMUSHOptions.Default());
		await Assert.That(analyzer.Hover("visible(1)", 0, 3)).IsNotNull();
		await Assert.That(analyzer.Hover("alias(1)", 0, 2)).IsNotNull();
		await Assert.That(analyzer.Hover("hidden(1)", 0, 2)).IsNull();
		await Assert.That(analyzer.Hover("local()", 0, 2)).IsNull();
		await Assert.That(analyzer.Validate("visible(1)").Count).IsEqualTo(0);
		await Assert.That(analyzer.SignatureHelp("visible(1,2)", 0, 11)!.Parameters.Count).IsEqualTo(2);
	}
}
