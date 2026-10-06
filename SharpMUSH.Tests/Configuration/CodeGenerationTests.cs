using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Tests to verify that code generation correctly replaces reflection usage
/// for configuration metadata and property access.
/// </summary>
public class CodeGenerationTests
{
	/// <summary>The accessor reads whatever options it is handed; the shipped defaults populate every property.</summary>
	private static SharpMUSHOptions Options => SharpMUSHOptions.Default();

	#region ConfigMetadata Tests

	[Test]
	public async Task ConfigMetadata_PropertyToAttributeName_ContainsAllProperties()
	{
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;

		await Assert.That(propertyToAttr).IsNotNull();
		await Assert.That(propertyToAttr.Count).IsGreaterThan(0);

		await Assert.That(propertyToAttr.ContainsKey("MudName")).IsTrue();
		await Assert.That(propertyToAttr.ContainsKey("PlayerStart")).IsTrue();
		await Assert.That(propertyToAttr.ContainsKey("NoisyWhisper")).IsTrue();
	}

	[Test]
	public async Task ConfigMetadata_AttributeToPropertyName_IsReverseMapping()
	{
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		var attrToProperty = ConfigMetadata.AttributeToPropertyName;

		await Assert.That(attrToProperty).IsNotNull();
		await Assert.That(attrToProperty.Count).IsEqualTo(propertyToAttr.Count);

		foreach (var (propName, attrName) in propertyToAttr)
		{
			await Assert.That(attrToProperty.ContainsKey(attrName)).IsTrue();
			await Assert.That(attrToProperty[attrName]).IsEqualTo(propName);
		}
	}

	[Test]
	public async Task ConfigMetadata_PropertyMetadata_ContainsMetadataForAllProperties()
	{
		var propertyMetadata = ConfigMetadata.PropertyMetadata;
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;

		await Assert.That(propertyMetadata).IsNotNull();
		await Assert.That(propertyMetadata.Count).IsEqualTo(propertyToAttr.Count);

		foreach (var propName in propertyToAttr.Keys)
		{
			await Assert.That(propertyMetadata.ContainsKey(propName)).IsTrue();
			var metadata = propertyMetadata[propName];
			await Assert.That(metadata).IsNotNull();
			await Assert.That(metadata.Name).IsNotEmpty();
		}
	}

	[Test]
	public async Task ConfigMetadata_SpecificProperty_HasCorrectMapping()
	{
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;

		await Assert.That(propertyToAttr["MudName"]).IsEqualTo("mud_name");

		await Assert.That(propertyToAttr["PlayerStart"]).IsEqualTo("player_start");
	}

	/// <summary>
	/// The generated Category must be the property that owns the category on SharpMUSHOptions ("Net"),
	/// which is also the value every SharpConfigAttribute declares and the value SchemaBuilder derives.
	/// It used to be the containing record's type name ("NetOptions"), so one API response described the
	/// same property two ways: "NetOptions" under metadata and "Net" under schema.
	/// </summary>
	[Test]
	public async Task ConfigMetadata_Category_IsTheOwningPropertyName()
	{
		await Assert.That(ConfigMetadata.PropertyMetadata["MudName"].Category).IsEqualTo("Net");
		await Assert.That(ConfigMetadata.PropertyMetadata["PlayerStart"].Category).IsEqualTo("Database");
		await Assert.That(ConfigMetadata.PropertyMetadata["NoisyWhisper"].Category).IsEqualTo("Command");
	}

	/// <summary>
	/// Every generated Category must agree with ConfigAccessor — the two tables come from one walk of the
	/// same members and must not drift.
	/// </summary>
	[Test]
	public async Task ConfigMetadata_Category_AgreesWithConfigAccessor()
	{
		var mismatches = ConfigMetadata.PropertyMetadata
			.Where(kv => kv.Value.Category != ConfigAccessor.GetCategoryForProperty(kv.Key))
			.Select(kv => $"{kv.Key}: metadata={kv.Value.Category} accessor={ConfigAccessor.GetCategoryForProperty(kv.Key)}")
			.ToList();

		await Assert.That(mismatches).IsEmpty();
	}

	/// <summary>
	/// The UI-shaping fields of SharpConfigAttribute have to survive generation, or SchemaBuilder cannot
	/// stop reflecting for them.
	/// </summary>
	[Test]
	public async Task ConfigMetadata_CarriesTheAttributesUiFields()
	{
		var mudName = ConfigMetadata.PropertyMetadata["MudName"];
		await Assert.That(mudName.Group).IsEqualTo("General");
		await Assert.That(mudName.Order).IsEqualTo(1);

		var port = ConfigMetadata.PropertyMetadata["Port"];
		await Assert.That(port.Group).IsEqualTo("Connection Settings");
		await Assert.That(port.ValidationPattern).IsEqualTo(@"^\d+$");
		await Assert.That(port.Min).IsEqualTo(1);
		await Assert.That(port.Max).IsEqualTo(65535);

		var sslPort = ConfigMetadata.PropertyMetadata["SslPort"];
		await Assert.That(sslPort.Tooltip).IsEqualTo("0 reports no TLS port");

		await Assert.That(ConfigMetadata.PropertyMetadata["PortalPort"].Unused).Contains("ASPNETCORE_URLS");
		await Assert.That(port.Unused).IsNull();
	}

	/// <summary>
	/// An empty <c>package_manager</c> does not disable package management: the installer and the
	/// profile-handler reset fall back to the seeded Package Manager (<see cref="DatabaseOptions.PackageManagerOrSeeded"/>).
	/// The admin page's tooltip and every shipped <c>mushcnf.dst</c> say so, rather than promising "disable".
	/// </summary>
	[Test]
	[Arguments("SharpMUSH.Configuration")]
	[Arguments("SharpMUSH.Tests/Configuration/Testfile")]
	[Arguments("SharpMUSH.Benchmarks")]
	public async Task AnEmptyPackageManager_IsDescribedAsTheSeededFallback_NotAsDisabled(string mushcnfDirectory)
	{
		var tooltip = ConfigMetadata.PropertyMetadata["PackageManager"].Tooltip;
		await Assert.That(tooltip).IsNotNull();
		await Assert.That(tooltip!).DoesNotContain("disable");
		await Assert.That(tooltip!).Contains($"#{DatabaseOptions.SeededPackageManager}");

		var lines = File.ReadAllLines(Path.Join(TestPaths.RepositoryRoot, mushcnfDirectory, "mushcnf.dst"));
		var start = Array.FindIndex(lines, line => line.StartsWith("# Package manager.", StringComparison.Ordinal));
		var end = Array.FindIndex(lines, start, line => line.StartsWith("package_manager ", StringComparison.Ordinal));
		var comment = string.Join('\n', lines[start..end]);
		await Assert.That(comment).DoesNotContain("disable");
		await Assert.That(comment).Contains("empty");
	}

	#endregion

	#region ConfigAccessor Tests

	[Test]
	public async Task ConfigAccessor_Categories_ContainsAllCategories()
	{
		var categories = ConfigAccessor.Categories;

		await Assert.That(categories).IsNotNull();
		await Assert.That(categories.Length).IsGreaterThan(0);

		await Assert.That(categories).Contains("Net");
		await Assert.That(categories).Contains("Database");
		await Assert.That(categories).Contains("Command");
		await Assert.That(categories).Contains("Chat");
	}

	[Test]
	public async Task ConfigAccessor_GetValue_ReturnsCorrectValue()
	{
		var options = Options;

		var mudNameValue = ConfigAccessor.GetValue(options, "MudName");
		await Assert.That(mudNameValue).IsNotNull();
		await Assert.That(mudNameValue).IsEqualTo(options.Net.MudName);

		var playerStartValue = ConfigAccessor.GetValue(options, "PlayerStart");
		await Assert.That(playerStartValue).IsNotNull();
		await Assert.That(playerStartValue).IsEqualTo(options.Database.PlayerStart);
	}

	[Test]
	public async Task ConfigAccessor_GetValue_InvalidProperty_ReturnsNull()
	{
		var options = Options;

		var invalidValue = ConfigAccessor.GetValue(options, "InvalidPropertyName123");
		await Assert.That(invalidValue).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_TryGetValue_SucceedsForValidProperty()
	{
		var options = Options;

		var success = ConfigAccessor.TryGetValue(options, "MudName", out var value);
		await Assert.That(success).IsTrue();
		await Assert.That(value).IsNotNull();
		await Assert.That(value).IsEqualTo(options.Net.MudName);
	}

	[Test]
	public async Task ConfigAccessor_TryGetValue_FailsForInvalidProperty()
	{
		var options = Options;

		var success = ConfigAccessor.TryGetValue(options, "InvalidPropertyName123", out var value);
		await Assert.That(success).IsFalse();
	}

	[Test]
	public async Task ConfigAccessor_GetPropertyType_ReturnsCorrectType()
	{
		var mudNameType = ConfigAccessor.GetPropertyType("MudName");
		await Assert.That(mudNameType).IsNotNull();
		await Assert.That(mudNameType).IsEqualTo(typeof(string));

		var playerStartType = ConfigAccessor.GetPropertyType("PlayerStart");
		await Assert.That(playerStartType).IsNotNull();
		await Assert.That(playerStartType).IsEqualTo(typeof(uint));

		var noisyWhisperType = ConfigAccessor.GetPropertyType("NoisyWhisper");
		await Assert.That(noisyWhisperType).IsNotNull();
		await Assert.That(noisyWhisperType).IsEqualTo(typeof(bool));
	}

	[Test]
	public async Task ConfigAccessor_GetPropertyType_InvalidProperty_ReturnsNull()
	{
		var invalidType = ConfigAccessor.GetPropertyType("InvalidPropertyName123");
		await Assert.That(invalidType).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_GetCategoryForProperty_ReturnsCorrectCategory()
	{
		var mudNameCategory = ConfigAccessor.GetCategoryForProperty("MudName");
		await Assert.That(mudNameCategory).IsEqualTo("Net");

		var playerStartCategory = ConfigAccessor.GetCategoryForProperty("PlayerStart");
		await Assert.That(playerStartCategory).IsEqualTo("Database");

		var noisyWhisperCategory = ConfigAccessor.GetCategoryForProperty("NoisyWhisper");
		await Assert.That(noisyWhisperCategory).IsEqualTo("Command");
	}

	[Test]
	public async Task ConfigAccessor_GetCategoryForProperty_InvalidProperty_ReturnsNull()
	{
		var invalidCategory = ConfigAccessor.GetCategoryForProperty("InvalidPropertyName123");
		await Assert.That(invalidCategory).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_AllProperties_CanBeAccessed()
	{
		var options = Options;
		var propertyNames = ConfigMetadata.PropertyToAttributeName.Keys;

		foreach (var propName in propertyNames)
		{
			var value = ConfigAccessor.GetValue(options, propName);
			var type = ConfigAccessor.GetPropertyType(propName);
			await Assert.That(type).IsNotNull();
		}
	}

	#endregion

	#region Integration Tests

	[Test]
	public async Task Integration_ConfigMetadataAndAccessor_WorkTogether()
	{
		var options = Options;

		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		var firstProperty = propertyToAttr.First();

		var value = ConfigAccessor.GetValue(options, firstProperty.Key);

		var type = ConfigAccessor.GetPropertyType(firstProperty.Key);
		await Assert.That(type).IsNotNull();

		if (value != null)
		{
			await Assert.That(type!.IsAssignableFrom(value.GetType())).IsTrue();
		}
	}

	[Test]
	public async Task Integration_AllMetadataProperties_HaveValidAccessors()
	{
		var options = Options;
		var propertyMetadata = ConfigMetadata.PropertyMetadata;

		foreach (var (propName, metadata) in propertyMetadata)
		{
			var value = ConfigAccessor.GetValue(options, propName);
			var type = ConfigAccessor.GetPropertyType(propName);
			var category = ConfigAccessor.GetCategoryForProperty(propName);

			await Assert.That(type).IsNotNull();
			await Assert.That(category).IsNotEmpty();

			await Assert.That(metadata.Name).IsNotEmpty();
		}
	}

	#endregion
}
