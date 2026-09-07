using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
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

	[Test]
	public async ValueTask Hide_TogglesConnectionHiddenState_ForAPrivilegedExecutor()
	{
		// Isolated player + handle so this test never touches shared God (#1)'s connection state.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideToggleConn");
		// @hide is permission-gated (CanHide: wizard/royalty or the Hide power) - grant WIZARD.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

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
}
