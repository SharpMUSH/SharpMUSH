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
		// Create a thing to test with
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create FlagTestThing2"));

		// Set NO_COMMAND flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set FlagTestThing2=NO_COMMAND"));

		// Test that "@set thing=!no_com" unsets the NO_COMMAND flag (partial match with negation)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set FlagTestThing2=!no_com"));

		// Verify using notification - the DebugVerboseTests style doesn't query back
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						s => s.ToPlainText()!.Contains("Unset", StringComparison.OrdinalIgnoreCase),
						s => s.Contains("Unset", StringComparison.OrdinalIgnoreCase))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
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
		// Create a thing to test with
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create FlagTestThing4"));

		// Set VISUAL flag first
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set FlagTestThing4=VISUAL"));

		// Test that "@set thing=!vis" unsets the VISUAL flag (partial match with negation)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set FlagTestThing4=!vis"));

		// Verify using notification - the DebugVerboseTests style doesn't query back
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf<MString, string>>(msg =>
					msg.Match(
						s => s.ToPlainText()!.Contains("Unset", StringComparison.OrdinalIgnoreCase),
						s => s.Contains("Unset", StringComparison.OrdinalIgnoreCase))),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
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
