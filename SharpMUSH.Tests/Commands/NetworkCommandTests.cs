using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NetworkCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask HttpCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@http https://example.com"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SqlCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sql SELECT * FROM test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask MapsqlCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mapsql SELECT * FROM test"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SitelockCommand()
	{
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@sockset #1=option"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SlaveCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@slave"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
