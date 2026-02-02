using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
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
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask SetFlag_PartialMatch_Color()
	{
		// Test that "@set #1=col" sets the COLOR flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=col"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.Value.ToArrayAsync();
		var hasColorFlag = flags.Any(x => x.Name == "COLOR");

		await Assert.That(hasColorFlag).IsTrue();
	}

	[Test]
	public async ValueTask SetFlag_PartialMatch_NoCommand()
	{
		// First, find a thing to test with (not a player)
		var thingResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create TestFlagThing"));
		var thingDbRef = DBRef.Parse(thingResult.Message!.ToPlainText()!);
		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));

		// Test that "@set thing=no_com" sets the NO_COMMAND flag
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=no_com"));

		var updatedThing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flags = await updatedThing.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasNoCommandFlag = flags.Any(x => x.Name == "NO_COMMAND");

		await Assert.That(hasNoCommandFlag).IsTrue();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_WithNegation()
	{
		// First, find a thing to test with (not a player)
		var thingResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create TestUnsetFlagThing"));
		var thingDbRef = DBRef.Parse(thingResult.Message!.ToPlainText()!);
		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));

		// Set NO_COMMAND flag first (using full name to be certain)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=NO_COMMAND"));

		var thingWithFlag = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flagsWithFlag = await thingWithFlag.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasFlag = flagsWithFlag.Any(x => x.Name == "NO_COMMAND");
		await Assert.That(hasFlag).IsTrue();

		// Now test that "@set thing=!no_com" unsets the NO_COMMAND flag
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=!no_com"));

		var updatedThing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flagsAfter = await updatedThing.AsThing.Object.Flags.Value.ToArrayAsync();
		var stillHasFlag = flagsAfter.Any(x => x.Name == "NO_COMMAND");

		await Assert.That(stillHasFlag).IsFalse();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_Color()
	{
		// Ensure COLOR flag is set first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=COLOR"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var flagsBefore = await one.AsPlayer.Object.Flags.Value.ToArrayAsync();
		var hasColorBefore = flagsBefore.Any(x => x.Name == "COLOR");
		await Assert.That(hasColorBefore).IsTrue();

		// Test that "@set #1=!col" unsets the COLOR flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=!col"));

		var oneAfter = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var flagsAfter = await oneAfter.AsPlayer.Object.Flags.Value.ToArrayAsync();
		var hasColorAfter = flagsAfter.Any(x => x.Name == "COLOR");

		await Assert.That(hasColorAfter).IsFalse();
	}

	[Test]
	public async ValueTask SetFlag_AliasPartialMatch_Colour()
	{
		// Test that "colo" matches "COLOR" (either via direct prefix match on COLOR or via alias COLOUR prefix match)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=colo"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var flags = await one.AsPlayer.Object.Flags.Value.ToArrayAsync();
		var hasColorFlag = flags.Any(x => x.Name == "COLOR");

		await Assert.That(hasColorFlag).IsTrue();
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
}
