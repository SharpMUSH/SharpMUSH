using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class UserDefinedCommandsTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>(); 
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	[Test]
	public async Task SetAndResetCacheTest()
	{
		await Parser!.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test:@pemit #1=Value 1 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test2:@pemit #1=Value 2 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test2"));

		await NotifyService!
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Value 1 received");
		await NotifyService!
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Value 2 received");
	}
}