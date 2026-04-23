using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for wildcard/partial flag matching in @set command
/// </summary>
public class FlagWildcardMatchingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask SetFlag_PartialMatch_NoCommand()
	{
		// Create a thing to test with
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create FlagTestThing1"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Test that "@set thing=no_com" sets the NO_COMMAND flag (partial match)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=no_com"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flags = await thing.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasNoCommandFlag = flags.Any(x => x.Name == "NO_COMMAND");

		await Assert.That(hasNoCommandFlag).IsTrue();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_NoCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a thing with a unique name so the flag-reset message is unique in the session.
		// Pattern B: "{uniqueName} - NO_COMMAND reset." appears exactly once across the session.
		var uniqueName = TestIsolationHelpers.GenerateUniqueName("FlagTest2");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));

		// Set NO_COMMAND flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {uniqueName}=NO_COMMAND"));

		// Test that "@set thing=!no_com" unsets the NO_COMMAND flag (partial match with negation)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {uniqueName}=!no_com"));

		// Pattern B: ManipulateSharpObjectService.SetOrUnsetFlag notifies with sender=null.
		// The message "{uniqueName} - NO_COMMAND reset." is globally unique due to the generated name.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextEquals(msg, $"{uniqueName} - NO_COMMAND reset.")),
				(AnySharpObject?)null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SetFlag_PartialMatch_Visual()
	{
		// Create a thing to test with
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create FlagTestThing3"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Test that "@set thing=vis" sets the VISUAL flag (partial match)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=vis"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flags = await thing.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasVisualFlag = flags.Any(x => x.Name == "VISUAL");

		await Assert.That(hasVisualFlag).IsTrue();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_Visual()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a thing with a unique name so the flag-reset message is unique in the session.
		// Pattern B: "{uniqueName} - VISUAL reset." appears exactly once across the session.
		var uniqueName = TestIsolationHelpers.GenerateUniqueName("FlagTest4");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));

		// Set VISUAL flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {uniqueName}=VISUAL"));

		// Test that "@set thing=!vis" unsets the VISUAL flag (partial match with negation)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {uniqueName}=!vis"));

		// Pattern B: ManipulateSharpObjectService.SetOrUnsetFlag notifies with sender=null.
		// The message "{uniqueName} - VISUAL reset." is globally unique due to the generated name.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg =>
					TestHelpers.MessagePlainTextEquals(msg, $"{uniqueName} - VISUAL reset.")),
				(AnySharpObject?)null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask GetObjectFlagQuery_ExactMatch_Preferred()
	{
		// If there are flags like "COLOR" and "COLORFUL" (hypothetically),
		// an exact match should be preferred over a partial match
		// Testing with COLOR which should match exactly
		var flag = await Mediator.Send(new GetObjectFlagQuery("COLOR"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("COLOR");
	}

	[Test]
	public async ValueTask GetObjectFlagQuery_PartialMatch()
	{
		// Test that "col" returns the COLOR flag
		var flag = await Mediator.Send(new GetObjectFlagQuery("COL"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("COLOR");
	}

	[Test]
	public async ValueTask GetObjectFlagQuery_CaseInsensitive()
	{
		// Test that lowercase "col" also works
		var flag = await Mediator.Send(new GetObjectFlagQuery("col"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("COLOR");
	}

	[Test]
	public async ValueTask GetObjectFlagQuery_AliasPartialMatch()
	{
		// Test that "colo" matches COLOR (could match via COLOR or its alias COLOUR)
		var flag = await Mediator.Send(new GetObjectFlagQuery("colo"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("COLOR");
	}
}
