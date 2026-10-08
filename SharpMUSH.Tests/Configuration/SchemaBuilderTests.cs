using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using System.Text.Json;

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
		return SchemaBuilder.BuildSchema();
	}

	[Test]
	public async Task StringArrayProperty_UsesStringListComponent()
	{
		var schema = BuildSchema();

		var playerFlags = schema.Properties["Flag.PlayerFlags"];

		await Assert.That(playerFlags.Type).IsEqualTo("array");
		await Assert.That(playerFlags.Component).IsEqualTo("stringlist");
	}

	/// <summary>A picture option is edited with the picture field, which shows it and offers the media library.</summary>
	[Test]
	[Arguments("Cosmetic.PortalLogo")]
	[Arguments("Cosmetic.PortalFavicon")]
	public async Task PictureProperty_UsesImageComponent(string path)
	{
		var property = BuildSchema().Properties[path];

		await Assert.That(property.Type).IsEqualTo("string");
		await Assert.That(property.Component).IsEqualTo("image");
		await Assert.That(property.DefaultValue).IsEqualTo(string.Empty).Because("empty is the SharpMUSH logo");
	}

	[Test]
	[Arguments("", true)]
	[Arguments("/api/wiki-assets/abc/crest.png", true)]
	[Arguments("https://example.com/crest.png", true)]
	[Arguments("//example.com/crest.png", false)]
	[Arguments("javascript:alert(1)", false)]
	[Arguments("crest.png", false)]
	[Arguments("/api/wiki-assets/abc/my crest.png", false)]
	public async Task PicturePattern_TakesAServerPathOrAWebAddress(string value, bool valid)
		=> await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(value, PortalPicture.Pattern)).IsEqualTo(valid);

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

	[Test]
	public async Task CategoryGroups_OrderedByFirstPropertyOrder_ThenEncounterOrder()
	{
		var schema = BuildSchema();

		var netGroups = string.Join("|", schema.Categories.First(c => c.Name == "Net").Groups.Select(g => g.Name));

		// First-property Orders in NetOptions: General=1, Database=1, Advanced=1,
		// Connection Settings=4, Network Protocol=4, Connection Limits=4.
		// Primary sort is that Order; ties keep property declaration order.
		await Assert.That(netGroups)
			.IsEqualTo("General|Database|Advanced|Connection Settings|Network Protocol|Connection Limits");
	}

	[Test]
	public async Task CategoryGroups_AllTiedOrders_KeepDeclarationOrder()
	{
		var schema = BuildSchema();

		var costGroups = string.Join("|", schema.Categories.First(c => c.Name == "Cost").Groups.Select(g => g.Name));

		await Assert.That(costGroups).IsEqualTo("Building Costs|Command Costs");
	}

	/// <summary>
	/// Every property reports the default <see cref="SharpMUSHOptions.Default"/> gives it — the one place a
	/// shipped default is written down. The schema used to read the record's constructor-parameter default
	/// instead, so the ~170 options whose record declares none (all of CommandOptions, DebugSharpParser)
	/// reported null and the admin page's "reset to default" had nothing to reset them to.
	/// </summary>
	[Test]
	public async Task DefaultValue_ComesFromTheShippedDefault()
	{
		var schema = BuildSchema();

		await Assert.That(schema.Properties["Net.MudName"].DefaultValue).IsEqualTo("SharpMUSH");
		await Assert.That(schema.Properties["Net.Port"].DefaultValue).IsEqualTo(4201u);
		await Assert.That((bool?)schema.Properties["Database.AllowBrowserCode"].DefaultValue).IsFalse();

		// Neither declares a parameter default on its record.
		await Assert.That((bool?)schema.Properties["Command.NoisyWhisper"].DefaultValue).IsFalse();
		await Assert.That((bool?)schema.Properties["Debug.DebugSharpParser"].DefaultValue).IsFalse();
	}

	/// <summary>Holds the claim above for every option, not just the ones named there.</summary>
	[Test]
	public async Task DefaultValue_MatchesTheShippedDefaultForEveryProperty()
	{
		var schema = BuildSchema();
		var shipped = SharpMUSHOptions.Default();

		var mismatches = schema.Properties.Values
			// Compared as JSON because arrays and dictionaries are fresh instances on every Default() call,
			// and JSON is how the admin page receives the default anyway.
			.Where(property => JsonSerializer.Serialize(property.DefaultValue)
				!= JsonSerializer.Serialize(ConfigAccessor.GetValue(shipped, property.Name)))
			.Select(property => property.Path)
			.ToArray();

		await Assert.That(string.Join(", ", mismatches)).IsEmpty();
	}

	[Test]
	[Arguments("player_flags", "Player Flags")]
	[Arguments("_port", "Port")]
	[Arguments("port_", "Port")]
	[Arguments("port__name", "Port Name")]
	public async Task FormatPropertyDisplayName_HandlesUnderscoreEdgeCases(string input, string expected)
	{
		var method = typeof(SchemaBuilder).GetMethod(
			"FormatPropertyDisplayName",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

		var result = (string?)method.Invoke(null, [input]);

		await Assert.That(result).IsEqualTo(expected);
	}
}
