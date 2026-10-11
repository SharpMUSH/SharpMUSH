using System.Text.Json;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Implementation.Generated;

namespace SharpMUSH.Tests.Integration.CodeAnalysis;

public class AnalysisCatalogTests
{
	[Test]
	public async Task OfflineCatalog_CarriesGeneratedMetadataAndAliasesWithoutEvaluation()
	{
		var catalog = BuiltInAnalysisCatalog.CreateSnapshot();
		await Assert.That(catalog.Source).IsEqualTo("offline");
		await Assert.That(catalog.Entries.Count).IsEqualTo(FunctionLibrary.CreateMetadata().Count + CommandLibrary.CreateMetadata().Count);
		var mean = catalog.Entries.Single(entry => entry.Kind == "function" && entry.Name.Equals("mean", StringComparison.OrdinalIgnoreCase));
		await Assert.That(mean.Aliases.Contains("avg")).IsTrue();
		var add = catalog.Entries.Single(entry => entry.Kind == "function" && entry.Name.Equals("add", StringComparison.OrdinalIgnoreCase));
		await Assert.That(add.MaxArgs is null).IsTrue();
		await Assert.That(add.MinArgs).IsEqualTo(FunctionLibrary.CreateMetadata()["add"].MinArgs);
		var json = JsonSerializer.Serialize(catalog);
		await Assert.That(json.Contains("2147483647")).IsFalse();
	}

	[Test]
	public async Task Snapshot_DefensivelyCopiesEntriesAndNestedMetadata()
	{
		var aliases = new[] { "alias" };
		var parameters = new[] { "arg" };
		var entries = new[] { new AnalysisCatalogEntry("name", "function", "built-in", 1, 2, aliases, parameters, [], "None", []) };
		var snapshot = new AnalysisCatalogSnapshot("epoch", "revision", "offline", entries);
		aliases[0] = "changed";
		parameters[0] = "changed";
		entries[0] = new AnalysisCatalogEntry("changed", "function", "built-in", 0, 0, [], [], [], "None", []);
		await Assert.That(snapshot.Entries[0].Name).IsEqualTo("name");
		await Assert.That(snapshot.Entries[0].Aliases[0]).IsEqualTo("alias");
		await Assert.That(snapshot.Entries[0].ParameterNames[0]).IsEqualTo("arg");
	}

	[Test]
	public async Task OfflineCapabilities_PreserveLargeDocumentsAndDoNotPromiseLiveCatalogOrSafeFormatting()
	{
		var capabilities = AnalysisCapabilities.Offline;
		await Assert.That(capabilities.MaxDocumentBytes >= 5 * 1024 * 1024).IsTrue();
		await Assert.That(capabilities.Source).IsEqualTo("offline");
		await Assert.That(capabilities.PositionEncoding).IsEqualTo("utf-16");
		await Assert.That(capabilities.Features.Contains("catalog-refresh")).IsFalse();
		await Assert.That(capabilities.Features.Contains("format")).IsFalse();
		await Assert.That(capabilities.Modes.Count).IsEqualTo(Enum.GetValues<MushAnalysisMode>().Length);
	}
}
