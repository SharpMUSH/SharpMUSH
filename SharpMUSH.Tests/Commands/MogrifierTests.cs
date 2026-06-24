using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MogrifierTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask ChannelMogrifier_SetCommand_ExecutesWithoutError()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/mogrifier test=#1"));
	}

	[Test]
	public async ValueTask ChannelMogrifier_ClearCommand_ExecutesWithoutError()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@channel/mogrifier test"));
	}

	[Test]
	public async ValueTask ChannelMessage_WithoutMogrifier_SendsBasicFormat()
	{
	}

	[Test]
	public async ValueTask MogrifyBlock_NonEmpty_BlocksMessage()
	{
	}

	[Test]
	public async ValueTask MogrifyOverride_True_SkipsChatFormat()
	{
	}

	[Test]
	public async ValueTask MogrifyFormat_CustomFormat_AltersMessage()
	{
	}

	[Test]
	public async ValueTask MogrifyParts_CustomValues_AlterComponents()
	{
	}

	[Test]
	public async ValueTask MogrifyUseLock_Fails_SkipsMogrification()
	{
	}
}
