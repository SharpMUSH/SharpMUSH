using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

public class AttributeWithInheritanceTests : TestsBase
{
	private IMediator Mediator => Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf()
	{
		// Create an object with an attribute
		var objResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestDirectAttr"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set an attribute on the object
		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&TEST_ATTR {objDbRef}=Direct Value"));

		// Query using the new method
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "TEST_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Verify
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(result.SourceObject.Number).IsEqualTo(objDbRef.Number);
		await Assert.That(result.Attributes.Length).IsEqualTo(1);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Direct Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf))]
	public async Task GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent()
	{
		// Create a parent object with an attribute
		var parentResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestParentAttr"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PARENT_ATTR {parentDbRef}=Parent Value"));

		// Create a child object
		var childResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestChildAttr"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		// Set parent
		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Query using the new method
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PARENT_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Verify
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.SourceObject.Number).IsEqualTo(parentDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Parent Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent))]
	public async Task GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone()
	{
		// Create a zone master with an attribute
		var zoneResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZoneAttr"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&ZONE_ATTR {zoneDbRef}=Zone Value"));

		// Create an object
		var objResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set zone
		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Query using the new method
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "ZONE_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Verify
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(result.SourceObject.Number).IsEqualTo(zoneDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Zone Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone))]
	public async Task GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject()
	{
		// Create a parent with attribute
		var parentResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheck"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&NO_PARENT_ATTR {parentDbRef}=Should Not Find"));

		// Create child
		var childResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheckChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Query with checkParent=false
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "NO_PARENT_ATTR" },
			CheckParent: false)).ToListAsync();

		// Should not find the attribute (empty list)
		await Assert.That(results.Count).IsEqualTo(0);
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject))]
	public async Task GetAttributeWithInheritance_ParentTakesPrecedenceOverZone()
	{
		// Create zone with attribute
		var zoneResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {zoneDbRef}=From Zone"));

		// Create parent with same attribute
		var parentResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {parentDbRef}=From Parent"));

		// Create child with both parent and zone
		var childResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@chzone {childDbRef}={zoneDbRef}"));

		// Query
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PREC_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Parent should take precedence
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("From Parent");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ParentTakesPrecedenceOverZone))]
	public async Task GetAttributeWithInheritance_NestedAttributes_WorksCorrectly()
	{
		// Test nested attributes like FOO`BAR
		var objResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create NestedAttrTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set nested attribute
		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&NESTED`ATTR {objDbRef}=Nested Value"));

		// Query using the new method
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "NESTED", "ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Verify
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(result.Attributes.Length).IsEqualTo(2);
		await Assert.That(result.Attributes[1].Value.ToPlainText()).IsEqualTo("Nested Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_NestedAttributes_WorksCorrectly))]
	public async Task GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence()
	{
		// Test complex hierarchy: Child <- Parent <- Grandparent
		// with zones at multiple levels
		
		// Create grandparent with attribute
		var grandparentResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexGrandparent"));
		var grandparentDbRef = DBRef.Parse(grandparentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&COMPLEX_ATTR {grandparentDbRef}=From Grandparent"));

		// Create parent with grandparent as parent
		var parentResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {parentDbRef}={grandparentDbRef}"));

		// Create child with parent
		var childResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Query - should find attribute from grandparent
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "COMPLEX_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		// Verify
		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.SourceObject.Number).IsEqualTo(grandparentDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("From Grandparent");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence))]
	public async Task GetAttributeWithInheritance_NonExistentAttribute_ReturnsNull()
	{
		// Test that non-existent attributes return null
		var objResult = await CommandParser.CommandParse(
			1,
			Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create NonExistentAttrTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Query for non-existent attribute
		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "DOES_NOT_EXIST" },
			CheckParent: true)).ToListAsync();

		// Verify (empty list when not found)
		await Assert.That(results.Count).IsEqualTo(0);
	}
}
