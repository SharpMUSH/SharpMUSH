using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Options that PennMUSH acts on and SharpMUSH used to accept and ignore (#2097): max_named_qregs,
/// use_muxcomm, max_player_chans and read_remote_desc. Each test runs against a command and function table of
/// its own, built with the options it needs, so the shared world's options stay as they are.
/// </summary>
public class ConfigOptionEffectTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private SharpMUSHOptions Baseline => Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;

	private sealed record FixedOptions(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	private async Task<(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser)> PlayerWith(
		SharpMUSHOptions options, string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);
		var wrapper = new FixedOptions(options);
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(Factory.Services, wrapper);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			Factory.Services, wrapper, functions);
		var parser = (MUSHCodeParser)Factory.CommandParserFor(player.DbRef, player.Handle) with
		{
			Configuration = wrapper,
			CommandLibrary = commands.Get(),
			FunctionLibrary = functions.Get()
		};
		return (player, parser);
	}

	private async Task<List<string>> Run(TestIsolationHelpers.TestPlayer player, IMUSHCodeParser parser,
		string command)
	{
		await parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		return Factory.Notifications.For(player.DbRef);
	}

	private static string ShortName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

	[Test]
	public async Task MaxNamedQRegs_RefusesANewNamedRegisterPastTheLimit()
	{
		var options = Baseline with { Limit = Baseline.Limit with { MaxNamedQRegisters = 2 } };
		var (player, parser) = await PlayerWith(options, "QregLimit");
		var token = TestIsolationHelpers.GenerateUniqueName("qreg");

		var heard = await Run(player, parser,
			$"think {token}:[setq(aa,1,bb,2)][setq(cc,3)]:[setq(aa,5)][r(aa)]:[setq(c,7)][r(c)]");

		// A third name is refused; one already set may change; a single-character register is not counted.
		await Assert.That(heard).Contains($"{token}:{ErrorMessages.Returns.TooManyRegisters}:5:7");
	}

	[Test]
	public async Task UseMuxCommOff_DisablesTheComsysCommands()
	{
		var options = Baseline with { Chat = Baseline.Chat with { UseMuxComm = false } };
		var (player, parser) = await PlayerWith(options, "MuxOff");

		var result = await parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"addcom {ShortName("a")}=Public"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.CommandDisabled);
		await Assert.That(Factory.Notifications.For(player.DbRef)).Contains(ErrorMessages.Notifications.CommandDisabled);
	}

	[Test]
	public async Task MaxPlayerChans_BoundsTheChannelsAMortalOwns()
	{
		var options = Baseline with { Chat = Baseline.Chat with { MaxPlayerChannels = 1 } };
		var (player, parser) = await PlayerWith(options, "ChanLimit");
		var first = ShortName("C");
		var second = ShortName("C");

		await Run(player, parser, $"@channel/add {first}=player");
		var heard = await Run(player, parser, $"@channel/add {second}=player");

		await Assert.That(heard).Contains(ErrorMessages.Notifications.ChatOwnTooManyChannels);
		await Assert.That(await Mediator.CreateStream(new SharpMUSH.Library.Queries.Database.GetChannelsOwnedByQuery(player.DbRef))
			.CountAsync()).IsEqualTo(1);

		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@channel/delete {first}"));
	}

	[Test]
	public async Task MaxPlayerChansZero_LeavesChannelCreationToStaff()
	{
		var (player, parser) = await PlayerWith(Baseline with { Chat = Baseline.Chat with { MaxPlayerChannels = 0 } }, "ChanNone");

		var heard = await Run(player, parser, $"@channel/add {ShortName("C")}=player");

		await Assert.That(heard).Contains(ErrorMessages.Notifications.ChatOwnTooManyChannels);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ReadRemoteDesc_DecidesARemoteObjectWithNoDescription(bool readRemoteDesc)
	{
		var options = Baseline with { Attribute = Baseline.Attribute with { ReadRemoteDesc = readRemoteDesc } };
		var (player, parser) = await PlayerWith(options, "RemoteDesc");
		var thing = await RemoteThing(ShortName("Far"));

		var heard = await Run(player, parser, $"look {thing}");

		await Assert.That(heard.Contains(ErrorMessages.Notifications.CantSeeThatFromHere)).IsEqualTo(!readRemoteDesc);
		await Assert.That(heard.Contains("You see nothing special.")).IsEqualTo(readRemoteDesc);
	}

	[Test]
	public async Task ARemoteDescriptionCanBeReadOnceItIsNotNearby()
	{
		var options = Baseline with { Attribute = Baseline.Attribute with { ReadRemoteDesc = false } };
		var (player, parser) = await PlayerWith(options, "RemoteNear");
		var described = await RemoteThing(ShortName("Far"));
		var nearby = await RemoteThing(ShortName("Near"));
		var token = TestIsolationHelpers.GenerateUniqueName("desc");
		await Admin($"@describe {described}=Seen_{token}");
		await Admin($"@describe {nearby}=Hidden_{token}");
		// DESCRIBE is NEARBY unless cleared, as PennMUSH's atr_tab.h has it.
		await Admin($"@set {described}/DESCRIBE=!nearby");

		var heard = await Run(player, parser, $"look {described}");
		var refused = await Run(player, parser, $"look {nearby}");

		await Assert.That(heard).Contains($"Seen_{token}");
		await Assert.That(refused).DoesNotContain($"Hidden_{token}");
		await Assert.That(refused).Contains(ErrorMessages.Notifications.CantSeeThatFromHere);
	}

	private async Task Admin(string command) =>
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));

	/// <summary>A thing in a room of its own, away from the player looking at it.</summary>
	private async Task<string> RemoteThing(string name)
	{
		var room = (await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {name}_Room")))
			.Message!.ToPlainText().Trim();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, name);
		await Admin($"@tel {thing}={room}");
		return thing.ToString();
	}
}
