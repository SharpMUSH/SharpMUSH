using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MiscCommandTests : TestClassFactory
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask VerbCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=greet,greets,greeting"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SweepCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sweep"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EditCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/DESC=old=new"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask GrepCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep #1=pattern"));

		// Verify that Notify was called at least once (could be "No matching attributes" or a list)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithPrintSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/print #1=pattern"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithWildSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/wild #1=*pattern*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithRegexpSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/regexp #1=.*pattern.*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithNocaseSwitch()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/nocase #1=PATTERN"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithAttributePattern()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep #1/DESC*=pattern"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask BriefCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("brief"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhoCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("who"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SessionCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("session"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuitCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("quit"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ConnectCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("connect player password"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PromptCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@prompt #1=Enter value:"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask NspromptCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@nsprompt #1=Enter value:"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
