using Mediator;
using SharpMUSH.Library.Commands.Database;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// <c>attrib_set()</c> prints the same "&lt;object&gt;/&lt;attr&gt; - Set." confirmation
/// <c>@set</c> does. PennMUSH's <c>fun_attrib_set</c> passes <c>0x01</c> to <c>do_set_atr</c>
/// (<c>src/fundb.c:2294-2300</c>), and that flag is precisely what asks for the line
/// (<c>src/attrib.c:2446-2452</c>).
///
/// <para>It goes to the EXECUTOR, not the enactor, and QUIET turns it off — on the player, on an
/// object they own, or on the attribute itself.</para>
/// </summary>
public class AttribSetNotificationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(Test)]
	public async Task CreatePlayer() =>
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<IMediator>(),
			ConnectionService, "AttribSetNotify");

	[After(Test)]
	public async Task CleanUpPrivatePlayer()
	{
		if (_player is null) return;
		await ConnectionService.Disconnect(_player.Handle);
		await WebAppFactoryArg.Services.GetRequiredService<IMediator>()
			.Send(new DeleteObjectCommand(_player.DbRef));
	}

	private async Task<IReadOnlyList<string>> RunAsPlayer(string command)
	{
		var recipient = _player.DbRef;
		var before = Notifications.CountFor(recipient);
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(recipient).Skip(before)];
	}

	[Test]
	public async Task AttribSet_ConfirmsTheSetToTheExecutor()
	{
		var said = await RunAsPlayer("think [attrib_set(me/ASN`LOUD,value)]");

		await Assert.That(said.Any(line => line.Contains("ASN`LOUD - Set.")))
			.IsTrue().Because("attrib_set() asks do_set_atr for the confirmation, exactly as @set does");
	}

	[Test]
	public async Task AttribSet_WithoutAValue_ReportsAClear()
	{
		await RunAsPlayer("think [attrib_set(me/ASN`GONE,value)]");
		var said = await RunAsPlayer("think [attrib_set(me/ASN`GONE)]");

		await Assert.That(said.Any(line => line.Contains("ASN`GONE - Cleared.")))
			.IsTrue().Because("no second argument clears the attribute, and the line says so");
	}

	[Test]
	public async Task AQuietAttribute_SetsSilently()
	{
		await RunAsPlayer("think [attrib_set(me/ASN`HUSH,value)]");
		await RunAsPlayer("@set me/ASN`HUSH=quiet");
		var said = await RunAsPlayer("think [attrib_set(me/ASN`HUSH,again)]");

		await Assert.That(said.Any(line => line.Contains("ASN`HUSH")))
			.IsFalse().Because("AF_QUIET on the attribute suppresses the confirmation");
	}
}
