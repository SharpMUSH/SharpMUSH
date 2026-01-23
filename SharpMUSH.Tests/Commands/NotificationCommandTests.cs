using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NotificationCommandTests : TestClassFactory
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask MessageCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Test message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask RespondCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond #1=Response"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask RwallCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@rwall Test message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WarningsCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@warnings"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WcheckCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wcheck #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SuggestCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest Test suggestion"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
