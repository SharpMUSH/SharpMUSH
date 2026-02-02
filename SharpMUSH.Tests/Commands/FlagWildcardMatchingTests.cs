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
/// Uses unique test objects to avoid state pollution from other tests
/// </summary>
[NotInParallel]
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
		// Create a unique player for this test
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate ColorTestPlayer1=testpass"));
		
		// Test that "@set *ColorTestPlayer1=col" sets the COLOR flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set *ColorTestPlayer1=col"));

		// Verify the flag was set
		var players = Mediator.CreateStream(new GetAllPlayersQuery());
		var testPlayer = await players.FirstOrDefaultAsync(p => p.Object.Name == "ColorTestPlayer1");
		
		await Assert.That(testPlayer).IsNotNull();
		var flags = await testPlayer!.Object.Flags.Value.ToArrayAsync();
		var hasColorFlag = flags.Any(x => x.Name == "COLOR");
		await Assert.That(hasColorFlag).IsTrue();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_Color()
	{
		// Create a unique player for this test
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate ColorTestPlayer2=testpass"));
		
		// Set COLOR flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set *ColorTestPlayer2=COLOR"));
		
		// Test that "@set *ColorTestPlayer2=!col" unsets the COLOR flag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set *ColorTestPlayer2=!col"));

		// Verify the flag was unset
		var players = Mediator.CreateStream(new GetAllPlayersQuery());
		var testPlayer = await players.FirstOrDefaultAsync(p => p.Object.Name == "ColorTestPlayer2");
		
		await Assert.That(testPlayer).IsNotNull();
		var flags = await testPlayer!.Object.Flags.Value.ToArrayAsync();
		var hasColorFlag = flags.Any(x => x.Name == "COLOR");
		await Assert.That(hasColorFlag).IsFalse();
	}

	[Test]
	public async ValueTask SetFlag_AliasPartialMatch_Colour()
	{
		// Create a unique player for this test
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate ColorTestPlayer3=testpass"));
		
		// Test that "colo" matches "COLOR" via prefix
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set *ColorTestPlayer3=colo"));

		// Verify the flag was set
		var players = Mediator.CreateStream(new GetAllPlayersQuery());
		var testPlayer = await players.FirstOrDefaultAsync(p => p.Object.Name == "ColorTestPlayer3");
		
		await Assert.That(testPlayer).IsNotNull();
		var flags = await testPlayer!.Object.Flags.Value.ToArrayAsync();
		var hasColorFlag = flags.Any(x => x.Name == "COLOR");
		await Assert.That(hasColorFlag).IsTrue();
	}

	[Test]
	public async ValueTask SetFlag_PartialMatch_NoCommand()
	{
		// Create a thing to test with
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create NoCommandTestThing1"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Test that "@set thing=no_com" sets the NO_COMMAND flag
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=no_com"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flags = await thing.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasNoCommandFlag = flags.Any(x => x.Name == "NO_COMMAND");

		await Assert.That(hasNoCommandFlag).IsTrue();
	}

	[Test]
	public async ValueTask UnsetFlag_PartialMatch_NoCommand()
	{
		// Create a thing to test with
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create NoCommandTestThing2"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Set NO_COMMAND flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=NO_COMMAND"));

		// Test that "@set thing=!no_com" unsets the NO_COMMAND flag
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=!no_com"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var flags = await thing.AsThing.Object.Flags.Value.ToArrayAsync();
		var hasNoCommandFlag = flags.Any(x => x.Name == "NO_COMMAND");

		await Assert.That(hasNoCommandFlag).IsFalse();
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
