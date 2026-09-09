using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Definitions;

/// <summary>
/// Pins <c>fail_lock</c>'s attribute names (<c>src/lock.c:832</c>): the four locks in
/// <c>lock_msgs</c> (<c>src/lock.c:102</c>) use their historical bases, everything else derives
/// <c>&lt;LOCKNAME&gt;_LOCK`&lt;type&gt;FAILURE</c> from the lock's PennMUSH name.
/// </summary>
public class LockMessagesTests
{
	[Test]
	[Arguments(LockType.Basic, "FAILURE", "OFAILURE", "AFAILURE")]
	[Arguments(LockType.Enter, "EFAIL", "OEFAIL", "AEFAIL")]
	[Arguments(LockType.Use, "UFAIL", "OUFAIL", "AUFAIL")]
	[Arguments(LockType.Leave, "LFAIL", "OLFAIL", "ALFAIL")]
	public async Task NamedLocksUseTheirHistoricalBases(
		LockType lockType, string what, string oWhat, string aWhat)
		=> await Assert.That(LockMessages.FailureAttributes(lockType))
			.IsEqualTo((what, oWhat, aWhat));

	[Test]
	[Arguments(LockType.Zone, "ZONE")]
	[Arguments(LockType.Page, "PAGE")]
	[Arguments(LockType.Drop, "DROP")]
	[Arguments(LockType.MailForward, "MAILFORWARD")]
	public async Task UnnamedLocksDeriveFromTheEnumMemberName(LockType lockType, string expected)
		=> await Assert.That(LockMessages.FailureAttributes(lockType))
			.IsEqualTo(($"{expected}_LOCK`FAILURE", $"{expected}_LOCK`OFAILURE", $"{expected}_LOCK`AFAILURE"));

	/// <summary>
	/// <c>Tport_Lock = "Teleport"</c> (<c>src/lock.c:61</c>), so the derived attribute is
	/// <c>TELEPORT_LOCK`FAILURE</c> even though SharpMUSH's <c>@lock</c> switch is <c>tport</c>.
	/// </summary>
	[Test]
	public async Task TPortDerivesFromPennsTeleportLockName()
		=> await Assert.That(LockMessages.FailureAttributes(LockType.Teleport))
			.IsEqualTo(("TELEPORT_LOCK`FAILURE", "TELEPORT_LOCK`OFAILURE", "TELEPORT_LOCK`AFAILURE"));

	[Test]
	public async Task EveryLockTypeProducesAName()
	{
		foreach (var lockType in Enum.GetValues<LockType>())
		{
			var (what, oWhat, aWhat) = LockMessages.FailureAttributes(lockType);

			await Assert.That(what).IsNotEmpty();
			await Assert.That(oWhat).IsNotEmpty();
			await Assert.That(aWhat).IsNotEmpty();
		}
	}
}
