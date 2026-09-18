using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@backup</c> through the command path. The name appeared in the suite exactly once before, inside
/// an assertion string in <c>WizardCommandTests</c> — the command itself had never been dispatched,
/// so neither its wizard lock nor its unsupported-provider branch had ever run.
///
/// <para>Copying a world is provider work with its own tests (<c>Database/Lightning/BackupTests</c>);
/// what is untested here is the command in front of it, which is why these assert the gate and the
/// two answers <c>/list</c> can give rather than the contents of a copy.</para>
/// </summary>
public class BackupCommandTests : ServerTestBase
{
	private IWorldBackupService BackupService => WebAppFactoryArg.Services.GetRequiredService<IWorldBackupService>();

	/// <summary>
	/// The command carries <c>CommandLock = "FLAG^WIZARD"</c>. The fixture runs as God, who passes it
	/// vacuously, so the gate is only exercised by driving a mortal on its own handle.
	/// </summary>
	/// <remarks>
	/// The assertion is the refusal itself, not the number of copies on disk. Counting is vacuous
	/// wherever the provider does not support backup at all: both sides of the comparison are zero,
	/// and a mortal that walked straight past the command lock into the unsupported-provider branch
	/// would have passed the test. That branch makes no copy either, so nothing downstream of the
	/// lock can stand in for the lock.
	/// </remarks>
	[Test]
	public async Task AMortalCannotRunIt()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(),
			ConnectionService,
			"BackupMortal");

		var before = BackupService.IsSupported ? BackupService.List().Count : 0;

		var answer = await CmdAs(mortal.DbRef, mortal.Handle, "@backup");

		await Assert.That(answer).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("the FLAG^WIZARD command lock must refuse a mortal before @backup runs at all");
		await Assert.That(BackupService.IsSupported ? BackupService.List().Count : 0).IsEqualTo(before)
			.Because("and no copy may reach the disk either");
	}

	/// <summary>
	/// <c>@backup/list</c> answers something in both worlds: the provider's copies where backup is
	/// supported, and why it is not where it is not. Silence would be the bug.
	/// </summary>
	[Test]
	public async Task ListAnswersWhetherOrNotBackupIsSupported()
	{
		// The recorder accumulates for the whole session, so read only what this command added.
		var before = Notifications.DeliveryCountFor(WebAppFactoryArg.ExecutorDBRef);

		await Cmd("@backup/list");

		var messages = Notifications.DeliveriesFor(WebAppFactoryArg.ExecutorDBRef)
			.Skip(before)
			.Select(delivery => delivery.Message)
			.ToList();

		await Assert.That(messages.Any(message =>
				message.Contains("@backup is not available here", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("No backups have been taken yet", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("Backups in ", StringComparison.OrdinalIgnoreCase)))
			.IsTrue();
	}
}
