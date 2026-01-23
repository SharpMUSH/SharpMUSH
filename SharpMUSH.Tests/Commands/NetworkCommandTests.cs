using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NetworkCommandTests : TestClassFactory
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask HttpCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@http https://example.com"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SqlCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask MapsqlCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql SELECT * FROM test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SitelockCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sitelock/list"));

		// Verify the command executed and sent output to the user
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), 
				Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SocksetCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sockset #1=option"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SlaveCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("@slave"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
