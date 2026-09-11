using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH reference: cmd_hide calls hide_player(executor, status, arg_left) (bsd.c:7161-7251).
/// @hide is a permission-gated (Can_Hide: wizard/royalty or the Hide @power), per-CONNECTION
/// toggle - not the DARK object flag. With no target it acts on every one of the executor's own
/// currently-open connections.
/// </summary>
public class HideCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	// Fixture privilege must not depend on the shared God connection remaining logged in.
	private async Task GrantWizardAsync(DBRef reference)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(player, wizard!))).IsTrue();
		await Assert.That(await (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>().CanHide()).IsTrue();
	}

	[Test]
	public async ValueTask Hide_TogglesConnectionHiddenState_ForAPrivilegedExecutor()
	{
		// Isolated player + handle so this test never touches shared God (#1)'s connection state.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideToggleConn");
		// @hide is permission-gated (CanHide: wizard/royalty or the Hide power) - grant WIZARD.
		await GrantWizardAsync(testPlayer.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		var isHidden = ConnectionService.Get(testPlayer.Handle)?.IsHidden;
		await Assert.That(isHidden).IsTrue();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		isHidden = ConnectionService.Get(testPlayer.Handle)?.IsHidden;
		await Assert.That(isHidden).IsFalse();
	}

	[Test]
	public async ValueTask Hide_PermissionDenied_ForAnUnprivilegedExecutor()
	{
		// A freshly created player has neither privilege nor the Hide power - CanHide() is false.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideNoPriv");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		// Permission denied - the connection's Hidden state must be untouched.
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsFalse();
	}

	[Test]
	public async ValueTask Hide_ExplicitOnAndOff_SetHiddenRegardlessOfCurrentState()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideExplicit");
		await GrantWizardAsync(testPlayer.DbRef);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsTrue();

		// /on again while already hidden: PennMUSH just re-applies and re-notifies, no "already" case.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsTrue();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsFalse();
	}

	/// <summary>
	/// bsd.c:7224-7232 - the no-switch (aggregate) toggle hides ALL of the player's connections if
	/// ANY of them is currently unhidden, and only unhides all of them once every connection was
	/// already hidden. With mixed state (one hidden, one visible) this is NOT the same as flipping
	/// each connection's own individual state.
	/// </summary>
	[Test]
	public async ValueTask Hide_NoSwitch_WithMultipleConnections_HidesAllWhenAnyIsVisible()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideMultiConn");
		await GrantWizardAsync(testPlayer.DbRef);

		// A second connection for the same player, already hidden; the first (testPlayer.Handle)
		// starts visible.
		var secondHandle = testPlayer.Handle + 500_000;
		await ConnectionService.Register(secondHandle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);
		await ConnectionService.Bind(secondHandle, testPlayer.DbRef);
		ConnectionService.Update(secondHandle, "Hidden", "1");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsTrue();
		await Assert.That(ConnectionService.Get(secondHandle)?.IsHidden).IsTrue();

		// Both connections are now hidden - the aggregate toggle unhides all of them.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsFalse();
		await Assert.That(ConnectionService.Get(secondHandle)?.IsHidden).IsFalse();
	}

	/// <summary>
	/// PennMUSH's alternate login words (<c>bsd.c:4431-4497</c>): <c>cd</c> connects and forces
	/// <c>DARK</c> on (and hides the connection if the player has permission), <c>cv</c> connects
	/// and forces <c>DARK</c> off, <c>ch</c> connects and hides the connection if permitted without
	/// touching <c>DARK</c>. Login runs at the connect screen (an unauthenticated handle), so this
	/// registers a fresh handle the way <c>ConnectScreenTests</c>/<c>SetPasswordConnectTests</c> do
	/// rather than using <see cref="TestIsolationHelpers.CreateTestPlayerWithHandleAsync"/>, which
	/// pre-binds the handle.
	/// </summary>
	[Test]
	[Arguments("cd", true, true)]   // command, expectDark, expectHidden
	[Arguments("cv", false, false)]
	[Arguments("ch", false, true)]
	public async ValueTask ConnectAlias_SetsExpectedDarkAndHiddenState(string connectWord, bool expectDark, bool expectHidden)
	{
		var playerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "ConnAlias");
		// cd/ch only hide the connection when the connecting player has Hide permission (CanHide:
		// wizard/royalty or the Hide power) - grant WIZARD so the hidden-state assertions are meaningful
		// for every alias under test, including cv (which never hides regardless of permission).
		await GrantWizardAsync(playerDbRef);

		var handle = Random.Shared.NextInt64(800_000, 899_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);

		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"{connectWord} {playerDbRef} TestPassword123"));

		// The login must have actually bound the handle - otherwise the mode-specific logic never ran
		// and the assertions below would be vacuous.
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsEqualTo(playerDbRef);

		var connectedPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
		await Assert.That(await connectedPlayer.HasFlag("DARK")).IsEqualTo(expectDark);
		await Assert.That(ConnectionService.Get(handle)?.IsHidden).IsEqualTo(expectHidden);
	}

	/// <summary>
	/// PennMUSH's set_flag special-cases DARK: only a Wizard or a player with the Can_Dark power may
	/// set it on a living player (flags.c ~1793-1798) - <c>cd</c> must respect that gate rather than
	/// force DARK on unconditionally. Unlike the WIZARD-granted scenario covered by
	/// <see cref="ConnectAlias_SetsExpectedDarkAndHiddenState"/>, this player has neither privilege nor
	/// the Can_Dark power, so the login must still succeed but DARK must be left untouched.
	/// </summary>
	[Test]
	public async ValueTask ConnectDark_ViaCd_DoesNotSetDarkForAnUnprivilegedPlayer()
	{
		var playerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "CdNoPriv");

		var handle = Random.Shared.NextInt64(800_000, 899_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8);

		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"cd {playerDbRef} TestPassword123"));

		// The login must have actually bound the handle - otherwise CanDark() never ran and this
		// assertion would be vacuous.
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsEqualTo(playerDbRef);

		var connectedPlayer = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Expect<AnySharpObject>();
		await Assert.That(await connectedPlayer.HasFlag("DARK")).IsFalse();
	}

	/// <summary>
	/// PennMUSH's <c>logout_sock</c> explicitly resets <c>d-&gt;hide = 0</c> (bsd.c:2248). Without that
	/// reset in <see cref="SharpMUSH.Library.Services.ConnectionService.Unbind"/>, a wizard who
	/// <c>@hide</c>s then <c>LOGOUT</c>s would leave the socket's <c>Hidden</c> metadata set for
	/// whoever connects next on that same handle - including a mortal with no permission to hide
	/// themselves, who would then show up excluded from WHO/mwho() despite never having run
	/// <c>@hide</c>.
	/// </summary>
	[Test]
	public async ValueTask Hide_ThenLogout_DoesNotLeakHiddenStateToTheNextLogin()
	{
		var firstPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideLogoutFirst");
		await GrantWizardAsync(firstPlayer.DbRef);
		await Parser.CommandParse(firstPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));
		await Assert.That(ConnectionService.Get(firstPlayer.Handle)?.IsHidden).IsTrue();

		await Parser.CommandParse(firstPlayer.Handle, ConnectionService, MarkupText.Plain("LOGOUT"));

		// Unbind must have cleared Hidden immediately, even before anyone reconnects on the handle.
		await Assert.That(ConnectionService.Get(firstPlayer.Handle)?.IsHidden).IsFalse();

		// A second, unprivileged player then connects on the SAME handle (mirroring a real socket
		// reused for a new login at the connect screen) and must not inherit the hidden state.
		var secondPlayerDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "HideLogoutSecond");
		var secondPlayerName = (await Mediator.Send(new GetObjectNodeQuery(secondPlayerDbRef))).Expect<AnySharpObject>().Object().Name;

		await Parser.CommandParse(firstPlayer.Handle, ConnectionService,
			MarkupText.Plain($"CONNECT {secondPlayerName} TestPassword123"));

		await Assert.That(ConnectionService.Get(firstPlayer.Handle)?.Ref).IsEqualTo(secondPlayerDbRef);
		await Assert.That(ConnectionService.Get(firstPlayer.Handle)?.IsHidden).IsFalse()
			.Because("the previous occupant's @hide must not leak across LOGOUT to the next login on the same handle");
	}
}
