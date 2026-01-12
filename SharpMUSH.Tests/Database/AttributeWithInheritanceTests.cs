using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

[NotInParallel]
public class AttributeWithInheritanceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf()
	{
		// Create an object with an attribute
		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestDirectAttr"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set an attribute on the object
		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&TEST_ATTR {objDbRef}=Direct Value"));

		// Query using the new method
		var result = await Mediator.Send(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "TEST_ATTR" },
			CheckParent: true));

		// Verify
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(result.SourceObject.Number).IsEqualTo(objDbRef.Number);
		await Assert.That(result.Attributes.Length).IsEqualTo(1);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Direct Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf))]
	public async Task GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent()
	{
		// Create a parent object with an attribute
		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestParentAttr"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PARENT_ATTR {parentDbRef}=Parent Value"));

		// Create a child object
		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestChildAttr"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		// Set parent
		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Query using the new method
		var result = await Mediator.Send(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PARENT_ATTR" },
			CheckParent: true));

		// Verify
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.SourceObject.Number).IsEqualTo(parentDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Parent Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent))]
	public async Task GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone()
	{
		// Create a zone master with an attribute
		var zoneResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZoneAttr"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&ZONE_ATTR {zoneDbRef}=Zone Value"));

		// Create an object
		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set zone
		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Query using the new method
		var result = await Mediator.Send(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "ZONE_ATTR" },
			CheckParent: true));

		// Verify
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(result.SourceObject.Number).IsEqualTo(zoneDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Zone Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone))]
	public async Task GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject()
	{
		// Create a parent with attribute
		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheck"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&NO_PARENT_ATTR {parentDbRef}=Should Not Find"));

		// Create child
		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheckChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Query with checkParent=false
		var result = await Mediator.Send(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "NO_PARENT_ATTR" },
			CheckParent: false));

		// Should not find the attribute
		await Assert.That(result).IsNull();
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject))]
	public async Task GetAttributeWithInheritance_ParentTakesPrecedenceOverZone()
	{
		// Create zone with attribute
		var zoneResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {zoneDbRef}=From Zone"));

		// Create parent with same attribute
		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {parentDbRef}=From Parent"));

		// Create child with both parent and zone
		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@chzone {childDbRef}={zoneDbRef}"));

		// Query
		var result = await Mediator.Send(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PREC_ATTR" },
			CheckParent: true));

		// Parent should take precedence
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("From Parent");
	}
}
