using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AdminCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask PcreateCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate TestPlayerPcreate=passwordPcreate"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask NewpasswordCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@newpassword #1=newpassNewpassword"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask PasswordCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@password oldpassPassword=newpassPassword"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ShutdownCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask RestartCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@restart"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask PurgeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask PoorCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poor #1001"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ReadcacheCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ChownallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1002=#2002"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ChzoneallCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzoneall #1003=#2003"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}
}
