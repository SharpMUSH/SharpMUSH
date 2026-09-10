using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Definitions;

/// <summary>
/// Pins <c>fail_lock</c>'s attribute names (<c>src/lock.c:832</c>): the four locks in
/// <c>lock_msgs</c> (<c>src/lock.c:102</c>) use their historical bases, everything else derives
/// <c>&lt;LOCKNAME&gt;_LOCK`&lt;type&gt;FAILURE</c> from the lock's PennMUSH name. Every derived member's
/// enum name matches that PennMUSH name once upper-cased, <see cref="LockType.Teleport"/> included:
/// <c>Tport_Lock = "Teleport"</c> (<c>src/lock.c:61</c>) gives <c>TELEPORT_LOCK`FAILURE</c>, though
/// SharpMUSH's <c>@lock</c> switch spelling is <c>tport</c>.
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
	[Arguments(LockType.Teleport, "TELEPORT")]
	public async Task UnnamedLocksDeriveFromTheEnumMemberName(LockType lockType, string expected)
		=> await Assert.That(LockMessages.FailureAttributes(lockType))
			.IsEqualTo(($"{expected}_LOCK`FAILURE", $"{expected}_LOCK`OFAILURE", $"{expected}_LOCK`AFAILURE"));

	/// <summary>
	/// Every <see cref="LockType"/>, spelled out. The two tests above cover nine members between
	/// them; this one is the exhaustiveness net, and it holds the literal attribute names rather
	/// than re-deriving them, so a renamed enum member or a member wrongly moved into or out of
	/// <c>lock_msgs</c> fails here instead of silently changing which attribute a lock triggers.
	/// </summary>
	private static readonly Dictionary<LockType, (string What, string OWhat, string AWhat)> Expected = new()
	{
		[LockType.Basic] = ("FAILURE", "OFAILURE", "AFAILURE"),
		[LockType.Enter] = ("EFAIL", "OEFAIL", "AEFAIL"),
		[LockType.Use] = ("UFAIL", "OUFAIL", "AUFAIL"),
		[LockType.Leave] = ("LFAIL", "OLFAIL", "ALFAIL"),
		[LockType.Zone] = ("ZONE_LOCK`FAILURE", "ZONE_LOCK`OFAILURE", "ZONE_LOCK`AFAILURE"),
		[LockType.Page] = ("PAGE_LOCK`FAILURE", "PAGE_LOCK`OFAILURE", "PAGE_LOCK`AFAILURE"),
		[LockType.Teleport] = ("TELEPORT_LOCK`FAILURE", "TELEPORT_LOCK`OFAILURE", "TELEPORT_LOCK`AFAILURE"),
		[LockType.Speech] = ("SPEECH_LOCK`FAILURE", "SPEECH_LOCK`OFAILURE", "SPEECH_LOCK`AFAILURE"),
		[LockType.Listen] = ("LISTEN_LOCK`FAILURE", "LISTEN_LOCK`OFAILURE", "LISTEN_LOCK`AFAILURE"),
		[LockType.Command] = ("COMMAND_LOCK`FAILURE", "COMMAND_LOCK`OFAILURE", "COMMAND_LOCK`AFAILURE"),
		[LockType.Parent] = ("PARENT_LOCK`FAILURE", "PARENT_LOCK`OFAILURE", "PARENT_LOCK`AFAILURE"),
		[LockType.Link] = ("LINK_LOCK`FAILURE", "LINK_LOCK`OFAILURE", "LINK_LOCK`AFAILURE"),
		[LockType.Drop] = ("DROP_LOCK`FAILURE", "DROP_LOCK`OFAILURE", "DROP_LOCK`AFAILURE"),
		[LockType.Give] = ("GIVE_LOCK`FAILURE", "GIVE_LOCK`OFAILURE", "GIVE_LOCK`AFAILURE"),
		[LockType.From] = ("FROM_LOCK`FAILURE", "FROM_LOCK`OFAILURE", "FROM_LOCK`AFAILURE"),
		[LockType.Pay] = ("PAY_LOCK`FAILURE", "PAY_LOCK`OFAILURE", "PAY_LOCK`AFAILURE"),
		[LockType.Receive] = ("RECEIVE_LOCK`FAILURE", "RECEIVE_LOCK`OFAILURE", "RECEIVE_LOCK`AFAILURE"),
		[LockType.Mail] = ("MAIL_LOCK`FAILURE", "MAIL_LOCK`OFAILURE", "MAIL_LOCK`AFAILURE"),
		[LockType.Follow] = ("FOLLOW_LOCK`FAILURE", "FOLLOW_LOCK`OFAILURE", "FOLLOW_LOCK`AFAILURE"),
		[LockType.Examine] = ("EXAMINE_LOCK`FAILURE", "EXAMINE_LOCK`OFAILURE", "EXAMINE_LOCK`AFAILURE"),
		[LockType.ChZone] = ("CHZONE_LOCK`FAILURE", "CHZONE_LOCK`OFAILURE", "CHZONE_LOCK`AFAILURE"),
		[LockType.Forward] = ("FORWARD_LOCK`FAILURE", "FORWARD_LOCK`OFAILURE", "FORWARD_LOCK`AFAILURE"),
		[LockType.Control] = ("CONTROL_LOCK`FAILURE", "CONTROL_LOCK`OFAILURE", "CONTROL_LOCK`AFAILURE"),
		[LockType.DropTo] = ("DROPTO_LOCK`FAILURE", "DROPTO_LOCK`OFAILURE", "DROPTO_LOCK`AFAILURE"),
		[LockType.Destroy] = ("DESTROY_LOCK`FAILURE", "DESTROY_LOCK`OFAILURE", "DESTROY_LOCK`AFAILURE"),
		[LockType.Interact] = ("INTERACT_LOCK`FAILURE", "INTERACT_LOCK`OFAILURE", "INTERACT_LOCK`AFAILURE"),
		[LockType.MailForward] = ("MAILFORWARD_LOCK`FAILURE", "MAILFORWARD_LOCK`OFAILURE", "MAILFORWARD_LOCK`AFAILURE"),
		[LockType.Take] = ("TAKE_LOCK`FAILURE", "TAKE_LOCK`OFAILURE", "TAKE_LOCK`AFAILURE"),
		[LockType.Open] = ("OPEN_LOCK`FAILURE", "OPEN_LOCK`OFAILURE", "OPEN_LOCK`AFAILURE"),
		[LockType.Filter] = ("FILTER_LOCK`FAILURE", "FILTER_LOCK`OFAILURE", "FILTER_LOCK`AFAILURE"),
		[LockType.InFilter] = ("INFILTER_LOCK`FAILURE", "INFILTER_LOCK`OFAILURE", "INFILTER_LOCK`AFAILURE"),
		[LockType.DropIn] = ("DROPIN_LOCK`FAILURE", "DROPIN_LOCK`OFAILURE", "DROPIN_LOCK`AFAILURE"),
		[LockType.ChOwn] = ("CHOWN_LOCK`FAILURE", "CHOWN_LOCK`OFAILURE", "CHOWN_LOCK`AFAILURE")
	};

	[Test]
	public async Task EveryLockTypeProducesTheNamesPennMUSHNames()
	{
		foreach (var lockType in Enum.GetValues<LockType>())
		{
			await Assert.That(Expected.ContainsKey(lockType)).IsTrue()
				.Because($"{lockType} is a lock type with no expected failure attributes written down");

			await Assert.That(LockMessages.FailureAttributes(lockType)).IsEqualTo(Expected[lockType]);
		}
	}
}
