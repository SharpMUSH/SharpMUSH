using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Integration;

/// <summary>
/// <c>attrib_set()</c> prints the same "&lt;object&gt;/&lt;attr&gt; - Set." confirmation
/// <c>@set</c> does. PennMUSH's <c>fun_attrib_set</c> passes <c>0x01</c> to <c>do_set_atr</c>
/// (<c>src/fundb.c:2294-2300</c>), and that flag is precisely what asks for the line
/// (<c>src/attrib.c:2446-2452</c>).
///
/// <para>It goes to the EXECUTOR, not the enactor, and QUIET is how a game turns it off — on the
/// player, on an object they own, or on the attribute itself. Deleting the notification instead
/// would have been a parity break; these pin both halves so it cannot be deleted by accident.</para>
/// </summary>
[NotInParallel]
public class AttribSetNotificationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<IReadOnlyList<string>> RunAsGod(string command)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));
		return [.. Notifications.For(god).Skip(before)];
	}

	[Test]
	public async Task AttribSet_ConfirmsTheSetToTheExecutor()
	{
		var said = await RunAsGod("think [attrib_set(me/ASN`LOUD,value)]");

		await Assert.That(said.Any(line => line.Contains("ASN`LOUD - Set.")))
			.IsTrue().Because("attrib_set() asks do_set_atr for the confirmation, exactly as @set does");
	}

	[Test]
	public async Task AttribSet_WithoutAValue_ReportsAClear()
	{
		await RunAsGod("think [attrib_set(me/ASN`GONE,value)]");
		var said = await RunAsGod("think [attrib_set(me/ASN`GONE)]");

		await Assert.That(said.Any(line => line.Contains("ASN`GONE - Cleared.")))
			.IsTrue().Because("no second argument clears the attribute, and the line says so");
	}

	[Test]
	public async Task AQuietAttribute_SetsSilently()
	{
		await RunAsGod("think [attrib_set(me/ASN`HUSH,value)]");
		await RunAsGod("@set me/ASN`HUSH=quiet");
		var said = await RunAsGod("think [attrib_set(me/ASN`HUSH,again)]");

		await Assert.That(said.Any(line => line.Contains("ASN`HUSH")))
			.IsFalse().Because("AF_QUIET on the attribute suppresses the confirmation");
	}
}
