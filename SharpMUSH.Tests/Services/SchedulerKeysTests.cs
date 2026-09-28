using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The queue's Quartz group and trigger names. These strings are not internal bookkeeping: they
/// are what a running scheduler already holds, so a change to their shape orphans every entry
/// in flight. Asserting the literals is the point of these tests, not an over-specification of
/// them.
/// </summary>
public class SchedulerKeysTests
{
	private static DbRefAttribute Target(int number, long created, params string[] attribute)
		=> new(new DBRef(number, created), attribute);

	/// <summary>
	/// A group is written at one site and read at another. Anything that makes the two disagree
	/// fails silently — the entry stops matching, so a semaphore never releases.
	/// </summary>
	[Test]
	public async Task ASemaphoreGroupRoundTripsThroughItsTarget()
	{
		var target = Target(7, 1744849096000, "SEMAPHORE");
		var group = SchedulerKeys.Semaphore(target);

		await Assert.That(group).IsEqualTo("semaphore:#7:1744849096000/SEMAPHORE");
		await Assert.That(SchedulerKeys.IsSemaphore(group)).IsTrue();
		await Assert.That(SchedulerKeys.SemaphoreTarget(group)).IsEqualTo(target);
	}

	/// <summary>An attribute tree keeps its <c>`</c> separators through the group name.</summary>
	[Test]
	public async Task ASemaphoreGroupCarriesAnAttributeTree()
	{
		var target = Target(7, 1744849096000, "FOO", "BAR");
		var group = SchedulerKeys.Semaphore(target);

		await Assert.That(group).IsEqualTo("semaphore:#7:1744849096000/FOO`BAR");
		await Assert.That(SchedulerKeys.SemaphoreTarget(group)).IsEqualTo(target);
	}

	[Test]
	public async Task ADelayGroupNamesTheWaitingExecutor()
	{
		var group = SchedulerKeys.Delay(new DBRef(5));

		await Assert.That(group).IsEqualTo("delay:#5");
		await Assert.That(SchedulerKeys.IsDelay(group)).IsTrue();
		await Assert.That(SchedulerKeys.IsSemaphore(group)).IsFalse();
	}

	/// <summary>
	/// The scopeless groups carry no payload, so neither prefix test may claim them.
	/// </summary>
	[Test]
	[Arguments(SchedulerKeys.EnqueueGroup, "enqueue")]
	[Arguments(SchedulerKeys.DirectInputGroup, "direct-input")]
	public async Task AScopelessGroupIsNeitherASemaphoreNorADelay(string group, string kind)
	{
		await Assert.That(SchedulerKeys.IsSemaphore(group)).IsFalse();
		await Assert.That(SchedulerKeys.IsDelay(group)).IsFalse();
		await Assert.That(SchedulerKeys.KindOf(group)).IsEqualTo(kind);
	}

	[Test]
	public async Task KindOfNamesEveryGroupShapeAndNothingElse()
	{
		await Assert.That(SchedulerKeys.KindOf(SchedulerKeys.Semaphore(Target(7, 1, "S")))).IsEqualTo("semaphore");
		await Assert.That(SchedulerKeys.KindOf(SchedulerKeys.Delay(new DBRef(5)))).IsEqualTo("delay");
		await Assert.That(SchedulerKeys.KindOf("something-else")).IsEqualTo("other");
	}

	/// <summary>
	/// An owner says who asked for the work, and the two prefixed forms have to be told apart:
	/// a connection handle drives the direct-input accounting, an object's dbref does not.
	/// </summary>
	[Test]
	public async Task AnOwnerIdentifiesEitherAHandleOrAnObject()
	{
		var byHandle = SchedulerKeys.Owner(42L);
		var byObject = SchedulerKeys.Owner(new DBRef(5, 1744849081000));

		await Assert.That(byHandle).IsEqualTo("handle:42");
		await Assert.That(byObject).IsEqualTo("dbref:#5:1744849081000");

		await Assert.That(SchedulerKeys.TryHandleOwner(byHandle, out var handle)).IsTrue();
		await Assert.That(handle).IsEqualTo(42L);

		await Assert.That(SchedulerKeys.TryHandleOwner(byObject, out _)).IsFalse()
			.Because("an object's owner is not a handle, and must not be read as one");
		await Assert.That(SchedulerKeys.TryHandleOwner(SchedulerKeys.SystemOwner, out _)).IsFalse();
		await Assert.That(SchedulerKeys.TryHandleOwner("handle:notanumber", out _)).IsFalse();
	}

	/// <summary>
	/// A trigger name is an owner and a PID, and the reporting path recovers the identity from it
	/// by stripping whichever prefix it carries.
	/// </summary>
	[Test]
	public async Task ATriggerNameIsAnOwnerAndAPid()
	{
		var trigger = SchedulerKeys.Trigger(new DBRef(5, 1744849081000), 16);

		await Assert.That(trigger).IsEqualTo("dbref:#5:1744849081000-16");
		await Assert.That(SchedulerKeys.WithoutOwnerPrefix(trigger).ToString()).IsEqualTo("#5:1744849081000-16");
		await Assert.That(SchedulerKeys.WithoutOwnerPrefix("handle:42-16").ToString()).IsEqualTo("42-16");
		await Assert.That(SchedulerKeys.WithoutOwnerPrefix("startup-16").ToString()).IsEqualTo("startup-16")
			.Because("a trigger name that carries no owner prefix is returned as it stands");
	}

	/// <summary>
	/// The Quartz jobs and <c>GetDelayTasks</c> read the PID back out of the trigger name. It is
	/// whatever follows the last dash, because the identity in front may carry dashes of its own.
	/// </summary>
	[Test]
	public async Task ATriggerNameGivesBackThePidItWasBuiltWith()
	{
		await Assert.That(SchedulerKeys.TriggerPid(SchedulerKeys.Trigger(new DBRef(5, 1744849081000), 16))).IsEqualTo(16L);
		await Assert.That(SchedulerKeys.TriggerPid(SchedulerKeys.Trigger((DBRef?)null, 16))).IsEqualTo(16L);
		await Assert.That(SchedulerKeys.TriggerPid(SchedulerKeys.TriggerName("async:#5/FOO-BAR", 16))).IsEqualTo(16L)
			.Because("an attribute name may contain a dash");
		await Assert.That(SchedulerKeys.TriggerName("input-session:3", 16)).IsEqualTo("input-session:3-16");
	}

	[Test]
	[Arguments("startup")]
	[Arguments("dbref:#5-")]
	[Arguments("dbref:#5-x")]
	[Arguments("dbref:#5-+16")]
	[Arguments("dbref:#5- 16")]
	public async Task ANameWithoutATrailingPidIsNotATrigger(string triggerName)
	{
		await Assert.That(SchedulerKeys.TryTriggerPid(triggerName, out _)).IsFalse();
		await Assert.That(() => SchedulerKeys.TriggerPid(triggerName)).Throws<FormatException>()
			.Because("a Quartz job only fires for triggers the queue named");
	}

	/// <summary><c>@ps</c> reports the executor a trigger name carries, and only a dbref counts.</summary>
	[Test]
	public async Task ATriggerNameGivesBackItsExecutor()
	{
		var executor = new DBRef(5, 1744849081000);

		await Assert.That(SchedulerKeys.TriggerExecutor(SchedulerKeys.Trigger(executor, 16))).IsEqualTo(executor);
		await Assert.That(SchedulerKeys.TriggerExecutor(SchedulerKeys.Trigger(new DBRef(5), 16))).IsEqualTo(new DBRef(5));
		await Assert.That(SchedulerKeys.TriggerExecutor(SchedulerKeys.Trigger((DBRef?)null, 16))).IsNull();
		await Assert.That(SchedulerKeys.TriggerExecutor(SchedulerKeys.TriggerName(SchedulerKeys.Owner(42L), 16))).IsNull()
			.Because("a connection handle is not an object");
		await Assert.That(SchedulerKeys.TriggerExecutor(SchedulerKeys.TriggerName("async:#5/FOO-BAR", 16))).IsNull();
	}

	/// <summary>
	/// A state with no executor rendered <c>dbref:</c> with nothing after it before these helpers
	/// existed, and still has to: the alternative silently renames triggers a running scheduler
	/// already holds.
	/// </summary>
	[Test]
	public async Task AnAbsentExecutorRendersEmptyRatherThanThrowing()
	{
		await Assert.That(SchedulerKeys.Owner((DBRef?)null)).IsEqualTo("dbref:");
		await Assert.That(SchedulerKeys.Trigger(null, 16)).IsEqualTo("dbref:-16");
		await Assert.That(SchedulerKeys.Delay(null)).IsEqualTo("delay:");
	}
}
