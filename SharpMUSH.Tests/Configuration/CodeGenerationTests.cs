using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Tests to verify that code generation correctly replaces reflection usage
/// for configuration metadata and property access.
/// </summary>
public class CodeGenerationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IOptionsWrapper<SharpMUSHOptions> Configuration => 
		WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	#region ConfigMetadata Tests

	[Test]
	public async Task ConfigMetadata_PropertyToAttributeName_ContainsAllProperties()
	{
		// Verify that the generated PropertyToAttributeName dictionary is populated
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		
		await Assert.That(propertyToAttr).IsNotNull();
		await Assert.That(propertyToAttr.Count).IsGreaterThan(0);
		
		// Verify some known properties exist
		await Assert.That(propertyToAttr.ContainsKey("MudName")).IsTrue();
		await Assert.That(propertyToAttr.ContainsKey("PlayerStart")).IsTrue();
		await Assert.That(propertyToAttr.ContainsKey("NoisyWhisper")).IsTrue();
	}

	[Test]
	public async Task ConfigMetadata_AttributeToPropertyName_IsReverseMapping()
	{
		// Verify that AttributeToPropertyName is the reverse of PropertyToAttributeName
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		var attrToProperty = ConfigMetadata.AttributeToPropertyName;
		
		await Assert.That(attrToProperty).IsNotNull();
		await Assert.That(attrToProperty.Count).IsEqualTo(propertyToAttr.Count);
		
		// Verify the reverse mapping works
		foreach (var (propName, attrName) in propertyToAttr)
		{
			await Assert.That(attrToProperty.ContainsKey(attrName)).IsTrue();
			await Assert.That(attrToProperty[attrName]).IsEqualTo(propName);
		}
	}

	[Test]
	public async Task ConfigMetadata_PropertyMetadata_ContainsMetadataForAllProperties()
	{
		// Verify that PropertyMetadata contains metadata for all properties
		var propertyMetadata = ConfigMetadata.PropertyMetadata;
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		
		await Assert.That(propertyMetadata).IsNotNull();
		await Assert.That(propertyMetadata.Count).IsEqualTo(propertyToAttr.Count);
		
		// Verify each property has metadata
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
		// Test a specific known property mapping
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		
		// MudName property should map to "mud_name" attribute
		await Assert.That(propertyToAttr["MudName"]).IsEqualTo("mud_name");
		
		// PlayerStart property should map to "player_start" attribute
		await Assert.That(propertyToAttr["PlayerStart"]).IsEqualTo("player_start");
	}

	#endregion

	#region ConfigAccessor Tests

	[Test]
	public async Task ConfigAccessor_Categories_ContainsAllCategories()
	{
		// Verify that the generated Categories array contains all configuration categories
		var categories = ConfigAccessor.Categories;
		
		await Assert.That(categories).IsNotNull();
		await Assert.That(categories.Length).IsGreaterThan(0);
		
		// Verify some known categories exist
		await Assert.That(categories).Contains("Net");
		await Assert.That(categories).Contains("Database");
		await Assert.That(categories).Contains("Command");
		await Assert.That(categories).Contains("Chat");
	}

	[Test]
	public async Task ConfigAccessor_GetValue_ReturnsCorrectValue()
	{
		// Verify that GetValue returns the correct value for a property
		var options = Configuration.CurrentValue;
		
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
		// Verify that GetValue returns null for invalid property names
		var options = Configuration.CurrentValue;
		
		var invalidValue = ConfigAccessor.GetValue(options, "InvalidPropertyName123");
		await Assert.That(invalidValue).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_TryGetValue_SucceedsForValidProperty()
	{
		// Verify that TryGetValue works correctly for valid properties
		var options = Configuration.CurrentValue;
		
		var success = ConfigAccessor.TryGetValue(options, "MudName", out var value);
		await Assert.That(success).IsTrue();
		await Assert.That(value).IsNotNull();
		await Assert.That(value).IsEqualTo(options.Net.MudName);
	}

	[Test]
	public async Task ConfigAccessor_TryGetValue_FailsForInvalidProperty()
	{
		// Verify that TryGetValue returns false for invalid properties
		var options = Configuration.CurrentValue;
		
		var success = ConfigAccessor.TryGetValue(options, "InvalidPropertyName123", out var value);
		await Assert.That(success).IsFalse();
	}

	[Test]
	public async Task ConfigAccessor_GetPropertyType_ReturnsCorrectType()
	{
		// Verify that GetPropertyType returns the correct type for properties
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
		// Verify that GetPropertyType returns null for invalid property names
		var invalidType = ConfigAccessor.GetPropertyType("InvalidPropertyName123");
		await Assert.That(invalidType).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_GetCategoryForProperty_ReturnsCorrectCategory()
	{
		// Verify that GetCategoryForProperty returns the correct category
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
		// Verify that GetCategoryForProperty returns null for invalid property names
		var invalidCategory = ConfigAccessor.GetCategoryForProperty("InvalidPropertyName123");
		await Assert.That(invalidCategory).IsNull();
	}

	[Test]
	public async Task ConfigAccessor_AllProperties_CanBeAccessed()
	{
		// Verify that all properties in the metadata can be accessed via ConfigAccessor
		var options = Configuration.CurrentValue;
		var propertyNames = ConfigMetadata.PropertyToAttributeName.Keys;
		
		foreach (var propName in propertyNames)
		{
			var value = ConfigAccessor.GetValue(options, propName);
			// Value might be null for nullable properties, but the call should not throw
			// Just verify the property name is recognized
			var type = ConfigAccessor.GetPropertyType(propName);
			await Assert.That(type).IsNotNull();
		}
	}

	#endregion

	#region Integration Tests

	[Test]
	public async Task Integration_ConfigMetadataAndAccessor_WorkTogether()
	{
		// Verify that ConfigMetadata and ConfigAccessor work together correctly
		var options = Configuration.CurrentValue;
		
		// Get a property name from metadata
		var propertyToAttr = ConfigMetadata.PropertyToAttributeName;
		var firstProperty = propertyToAttr.First();
		
		// Use ConfigAccessor to get the value
		var value = ConfigAccessor.GetValue(options, firstProperty.Key);
		
		// Verify we got a value (even if null for nullable types)
		var type = ConfigAccessor.GetPropertyType(firstProperty.Key);
		await Assert.That(type).IsNotNull();
		
		// If the value is not null, verify it matches the type
		if (value != null)
		{
			await Assert.That(type!.IsAssignableFrom(value.GetType())).IsTrue();
		}
	}

	[Test]
	public async Task Integration_AllMetadataProperties_HaveValidAccessors()
	{
		// Verify every property in ConfigMetadata can be accessed via ConfigAccessor
		var options = Configuration.CurrentValue;
		var propertyMetadata = ConfigMetadata.PropertyMetadata;
		
		foreach (var (propName, metadata) in propertyMetadata)
		{
			// Verify accessor methods work
			var value = ConfigAccessor.GetValue(options, propName);
			var type = ConfigAccessor.GetPropertyType(propName);
			var category = ConfigAccessor.GetCategoryForProperty(propName);
			
			// All accessors should return non-null metadata
			await Assert.That(type).IsNotNull();
			await Assert.That(category).IsNotEmpty();
			
			// Metadata should match
			await Assert.That(metadata.Name).IsNotEmpty();
		}
	}

	#endregion
}
