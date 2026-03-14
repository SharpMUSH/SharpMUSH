using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MiscCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask VerbCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=greet,greets,greeting"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SweepCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sweep"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EditCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@edit #1/DESC=old=new"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask GrepCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep #1=pattern"));

		// Verify that Notify was called at least once (could be "No matching attributes" or a list)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithPrintSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/print #1=pattern"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithWildSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/wild #1=*pattern*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithRegexpSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/regexp #1=.*pattern.*"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithNocaseSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@grep/nocase #1=PATTERN"));

		// Verify that Notify was called at least once
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask GrepCommand_WithAttributePattern()
	{
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("brief"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhoCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("who"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SessionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("session"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuitCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("quit"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ConnectCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("connect player password"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PromptCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@prompt #1=Enter value:"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask NspromptCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@nsprompt #1=Enter value:"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
