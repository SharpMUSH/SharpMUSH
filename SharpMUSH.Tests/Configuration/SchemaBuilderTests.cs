using SharpMUSH.Configuration;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Verifies that <see cref="SchemaBuilder"/> classifies collection-typed configuration
/// properties (string arrays and string-array dictionaries) with dedicated UI components,
/// so the config pages render editable element lists instead of a bare "string[]" type name.
/// </summary>
public class SchemaBuilderTests
{
	private static ConfigurationSchema BuildSchema()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);
		return SchemaBuilder.BuildSchema(options);
	}

	[Test]
	public async Task StringArrayProperty_UsesStringListComponent()
	{
		var schema = BuildSchema();

		var playerFlags = schema.Properties["Flag.PlayerFlags"];

		await Assert.That(playerFlags.Type).IsEqualTo("array");
		await Assert.That(playerFlags.Component).IsEqualTo("stringlist");
	}

	[Test]
	public async Task StringArrayDictionaryProperty_UsesDictionaryComponent()
	{
		var schema = BuildSchema();

		var functionAliases = schema.Properties["Alias.FunctionAliases"];

		await Assert.That(functionAliases.Type).IsEqualTo("dictionary");
		await Assert.That(functionAliases.Component).IsEqualTo("dictionary");
	}

	[Test]
	public async Task ScalarProperties_KeepTheirExistingComponents()
	{
		var schema = BuildSchema();

		await Assert.That(schema.Properties["Cosmetic.AnnounceConnects"].Component).IsEqualTo("switch");
		await Assert.That(schema.Properties["Cosmetic.MoneySingular"].Component).IsEqualTo("text");
	}
}
