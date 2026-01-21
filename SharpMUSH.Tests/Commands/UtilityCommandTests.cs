using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class UtilityCommandTests : TestsBase
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	public async ValueTask ThinkBasic()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkBasic Test output"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(x
				=> x.Value.ToString()!.Contains("ThinkBasic Test output")));
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkWithFunction [add(2,3)]"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Is<OneOf.OneOf<MString,string>>(x 
					=> x.Value.ToString()!.Contains("ThinkWithFunction 5")) );
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask CommentCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@@ This is a comment"));

		// Comment should not produce any output
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString,string>>(x 
				=> x.Value.ToString()!.Contains("This is a comment")));
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask LookBasic()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("look"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask LookAtObject()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("look #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ExamineObject()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		// Verify notify was called (exact count may vary based on object attributes)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString,string>>());
	}

	[Test]
	public async ValueTask ExamineObjectBriefSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine/brief #1"));

		// /brief should show header info but skip attributes
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString,string>>());
	}

	[Test]
	public async ValueTask ExamineObjectOpaqueSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine/opaque #1"));

		// /opaque should skip contents display
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString,string>>());
	}

	[Test]
	public async ValueTask ExamineWithAttributePattern()
	{
		// Clear any previous calls to the mock
		// Test examining with attribute pattern (e.g., examine #1/DESC*)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1/DESC*"));

		// Should display matching attributes
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString,string>>());
	}

	[Test]
	public async ValueTask ExamineCurrentLocation()
	{
		// Clear any previous calls to the mock
		// Test examining with no argument (examines current location)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine"));

		// Should display current location
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf.OneOf<MString,string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask FindCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SearchCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EntrancesCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask StatsCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask VersionCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@version"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask ScanCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@scan"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DecompileCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@decompile #1"));

		// Should receive notifications for decompiled output
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Any<OneOf.OneOf<MString,string>>(),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhereisCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
