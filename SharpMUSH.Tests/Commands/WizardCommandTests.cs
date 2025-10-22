using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class WizardCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask HaltCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allhalt"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@drain #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask PsCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask PsWithTarget()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask TriggerCommand()
	{
		// Set an attribute first
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TRIGGER_TEST #1=think Triggered!"));
		
		// Trigger it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger #1/TRIGGER_TEST"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force #1=think Forced!"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask NotifyCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@notify #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wait 1=think Waited"));

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask UptimeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@uptime"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DbckCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dbck"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DumpCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dump"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask QuotaCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@quota #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask AllquotaCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allquota"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask BootCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@boot #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
