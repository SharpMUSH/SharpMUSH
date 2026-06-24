using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

public class AttributeWithInheritanceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf()
	{
		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestDirectAttr"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&TEST_ATTR {objDbRef}=Direct Value"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "TEST_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(result.SourceObject.Number).IsEqualTo(objDbRef.Number);
		await Assert.That(result.Attributes.Length).IsEqualTo(1);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Direct Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf))]
	public async Task GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent()
	{
		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestParentAttr"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PARENT_ATTR {parentDbRef}=Parent Value"));

		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestChildAttr"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PARENT_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.SourceObject.Number).IsEqualTo(parentDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Parent Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent))]
	public async Task GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone()
	{
		var zoneResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZoneAttr"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&ZONE_ATTR {zoneDbRef}=Zone Value"));

		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "ZONE_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(result.SourceObject.Number).IsEqualTo(zoneDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("Zone Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone))]
	public async Task GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject()
	{
		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheck"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&NO_PARENT_ATTR {parentDbRef}=Should Not Find"));

		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create TestNoParentCheckChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "NO_PARENT_ATTR" },
			CheckParent: false)).ToListAsync();

		await Assert.That(results.Count).IsEqualTo(0);
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject))]
	public async Task GetAttributeWithInheritance_ParentTakesPrecedenceOverZone()
	{
		var zoneResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {zoneDbRef}=From Zone"));

		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create PrecedenceParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&PREC_ATTR {parentDbRef}=From Parent"));

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

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "PREC_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("From Parent");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ParentTakesPrecedenceOverZone))]
	public async Task GetAttributeWithInheritance_NestedAttributes_WorksCorrectly()
	{
		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create NestedAttrTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&NESTED`ATTR {objDbRef}=Nested Value"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "NESTED", "ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(result.Attributes.Length).IsEqualTo(2);
		await Assert.That(result.Attributes[1].Value.ToPlainText()).IsEqualTo("Nested Value");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_NestedAttributes_WorksCorrectly))]
	public async Task GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence()
	{
		var grandparentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexGrandparent"));
		var grandparentDbRef = DBRef.Parse(grandparentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"&COMPLEX_ATTR {grandparentDbRef}=From Grandparent"));

		var parentResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {parentDbRef}={grandparentDbRef}"));

		var childResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create ComplexChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			childDbRef,
			new[] { "COMPLEX_ATTR" },
			CheckParent: true)).ToListAsync();
		var result = results[0];

		await Assert.That(result.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(result.SourceObject.Number).IsEqualTo(grandparentDbRef.Number);
		await Assert.That(result.Attributes[0].Value.ToPlainText()).IsEqualTo("From Grandparent");
	}

	[Test]
	[DependsOn(nameof(GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence))]
	public async Task GetAttributeWithInheritance_NonExistentAttribute_ReturnsNull()
	{
		var objResult = await WebAppFactoryArg.CommandParser.CommandParse(
			1,
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IConnectionService>(),
			MModule.single("@create NonExistentAttrTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		var results = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(
			objDbRef,
			new[] { "DOES_NOT_EXIST" },
			CheckParent: true)).ToListAsync();

		await Assert.That(results.Count).IsEqualTo(0);
	}
}
