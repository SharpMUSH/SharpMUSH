using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Exits on a zone master room work in every room zoned to it, and the master room's exits work everywhere, after the
/// room's own exits. Reference: <c>src/game.c</c> process_command (remote_exit, global_exit) and <c>src/move.c</c>
/// move_wrapper.
/// </summary>
public class RemoteExitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private string MasterRoom => $"#{WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>()
		.CurrentValue.Database.MasterRoom}";

	private async Task<string> God(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText().Trim();

	private async Task<string> Dig(string prefix)
		=> Bare(await God($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<string> LocationOf(TestIsolationHelpers.TestPlayer who)
		=> Bare((await GodParser.FunctionParse(MarkupText.Plain($"[loc({who.DbRef})]")))!.Message.ToPlainText().Trim());

	private static string Bare(string reference)
		=> reference.IndexOf(':') is var colon and >= 0 ? reference[..colon] : reference;

	/// <summary>A room zoned to a zone master room, the master room, a room beyond, and a mover in the first room.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mover, string Here, string Zone, string There)> Zoned(string prefix)
	{
		var zone = await Dig($"{prefix}Zone");
		var here = await Dig($"{prefix}Here");
		var there = await Dig($"{prefix}There");
		await God($"@chzone {here}={zone}");

		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Mover");
		await God($"@teleport/silent {mover.DbRef}={here}");
		return (mover, here, zone, there);
	}

	[Test]
	public async ValueTask TypingAZoneMasterRoomsExitTakesIt()
	{
		var (mover, _, zone, there) = await Zoned("ZoneExit");
		var exit = TestIsolationHelpers.GenerateUniqueName("ZoneGate");
		await God($"@open {exit}={there},,{zone}");

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain(exit));

		await Assert.That(await LocationOf(mover)).IsEqualTo(there);
	}

	[Test]
	public async ValueTask GotoTakesAZoneMasterRoomsExit()
	{
		var (mover, _, zone, there) = await Zoned("ZoneGoto");
		var exit = TestIsolationHelpers.GenerateUniqueName("ZoneGotoGate");
		await God($"@open {exit}={there},,{zone}");

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain($"goto {exit}"));

		await Assert.That(await LocationOf(mover)).IsEqualTo(there);
	}

	[Test]
	public async ValueTask TheRoomsOwnExitWinsOverTheZonesExitOfTheSameName()
	{
		var (mover, here, zone, there) = await Zoned("ZoneShadow");
		var local = await Dig("ZoneShadowLocal");
		var exit = TestIsolationHelpers.GenerateUniqueName("ZoneShadowGate");
		await God($"@open {exit}={there},,{zone}");
		await God($"@open {exit}={local},,{here}");

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain(exit));

		await Assert.That(await LocationOf(mover)).IsEqualTo(local);
	}

	[Test]
	public async ValueTask AZoneExitIsNotTakenFromARoomOutsideTheZone()
	{
		var (_, _, zone, there) = await Zoned("ZoneOutside");
		var outside = await Dig("ZoneOutsideRoom");
		var exit = TestIsolationHelpers.GenerateUniqueName("ZoneOutsideGate");
		await God($"@open {exit}={there},,{zone}");
		var stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZoneOutsideMover");
		await God($"@teleport/silent {stranger.DbRef}={outside}");

		await GodParser.CommandParse(stranger.Handle, ConnectionService, MarkupText.Plain(exit));

		await Assert.That(await LocationOf(stranger)).IsEqualTo(outside);
	}

	[Test]
	public async ValueTask TypingAGlobalExitTakesIt()
	{
		var (mover, _, _, there) = await Zoned("GlobalExit");
		var exit = TestIsolationHelpers.GenerateUniqueName("GlobalGate");
		await God($"@open {exit}={there},,{MasterRoom}");

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain(exit));

		await Assert.That(await LocationOf(mover)).IsEqualTo(there);
	}

	[Test]
	public async ValueTask AZoneExitWinsOverAGlobalExitOfTheSameName()
	{
		var (mover, _, zone, there) = await Zoned("ZoneOverGlobal");
		var global = await Dig("ZoneOverGlobalElsewhere");
		var exit = TestIsolationHelpers.GenerateUniqueName("ZoneOverGlobalGate");
		await God($"@open {exit}={global},,{MasterRoom}");
		await God($"@open {exit}={there},,{zone}");

		await GodParser.CommandParse(mover.Handle, ConnectionService, MarkupText.Plain(exit));

		await Assert.That(await LocationOf(mover)).IsEqualTo(there);
	}
}
