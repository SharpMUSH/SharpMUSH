using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		public const string QueueControlSyntax = "Use @queue/list [pid], @queue/pause pid=reason, or @queue/resume pid; add /owner or /object for a target batch.";
		public const string QueueControlBadReason = "Pause reasons must contain at most 160 characters and no control characters.";
		public const string QueueControlOutcome = "PID {0}: {1}.";
		public const string QueueControlEntry = "PID {0} source {1} owner {2} {3} {4}; remaining {5}s; signalled {6}; {7}";
		public const string QueueControlTruncated = "Showing or processing at most 200 entries. Narrow the selection; repeat a batch operation for remaining eligible entries.";
		public const string QueueControlEmpty = "No accessible matching queue entries.";
		public const string QueuePausedHint = "Paused jobs: {0}. Use @queue/list for pause details and @queue/pause or @queue/resume to manage pending jobs.";
		public const string QueueRejected = "Queue admission rejected: {0}.";
		/// <summary>PennMUSH <c>process_expression</c> (<c>src/parse.c:2083-2084</c>): to a queue entry's enactor, unless QUIET, when the entry runs past <c>queue_entry_cpu_time</c>.</summary>
		public const string CpuUsageExceeded = "CPU usage exceeded.";
		public const string QueueUsage = "Admitted jobs: {0}; global limit: {1}; per-owner limit: {2}.";
		public const string QueueRejections = "Queue rejections ({0}): {1}.";

		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2272-2273</c>): to the enactor who halted an object someone else owns. <c>{0}</c> owner, <c>{1}</c> object, <c>{2}</c> its <c>#dbref</c>.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedOthersObjectFormat = "Halted: {0}'s {1}({2})";
		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2274-2275</c>): to the owner of an object someone else halted.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectByFormat = "Halted: {0}({1}), by {2}";
		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2259</c>): <c>@halt me</c>.</summary>
		public const string AllYourObjectsHalted = "All of your objects have been halted.";
		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2261</c>): to the enactor who halted another player.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllObjectsForPlayerHaltedFormat = "All objects for {0} have been halted.";
		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2263</c>): to the player someone else halted.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllYourObjectsHaltedByFormat = "All of your objects have been halted by {0}.";
		/// <summary>PennMUSH <c>do_halt1</c> (<c>src/cque.c:2249-2251</c>): replacement actions need control of the object.</summary>
		public const string HaltCommandNotAllowed = "You may not use @halt obj=command on this object.";
		/// <summary>PennMUSH <c>do_haltpid</c> (<c>src/cque.c:2294</c>, <c>:2301</c>): not a number, or no queue entry has it.</summary>
		public const string HaltInvalidPid = "That is not a valid pid!";
		/// <summary>PennMUSH <c>do_haltpid</c> (<c>src/cque.c:2335</c>).</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltPidHaltedFormat = "Queue entry with pid {0} halted.";
		/// <summary>PennMUSH <c>do_allhalt</c> (<c>src/cque.c:2347-2348</c>).</summary>
		public const string HaltWorldPowerDenied = "You do not have the power to bring the world to a halt.";
		/// <summary>PennMUSH <c>do_allhalt</c> (<c>src/cque.c:2353</c>), to every player. Penn's string has no full stop.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GloballyHaltedByFormat = "Your objects have been globally halted by {0}";
		/// <summary>PennMUSH <c>do_allrestart</c> (<c>src/cque.c:2371</c>).</summary>
		public const string RestartWorldPowerDenied = "You do not have the power to restart the world.";
		/// <summary>PennMUSH <c>do_allrestart</c> (<c>src/cque.c:2382</c>), to every player. Penn's string has no full stop.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GloballyRestartedByFormat = "Your objects are being globally restarted by {0}";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2425</c>): to the enactor who restarted another player.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllObjectsForPlayerRestartingFormat = "All objects for {0} are being restarted.";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2427-2429</c>): to the player someone else restarted.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AllYourObjectsRestartingByFormat = "All of your objects are being restarted by {0}.";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2435</c>): to the enactor who restarted an object someone else owns.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RestartingOthersObjectFormat = "Restarting: {0}'s {1}({2})";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2437</c>): to the owner of an object someone else restarted.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RestartingObjectByFormat = "Restarting: {0}({1}), by {2}";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2442</c>): <c>@restart me</c>.</summary>
		public const string AllYourObjectsRestarting = "All of your objects are being restarted.";
		/// <summary>PennMUSH <c>do_restart_com</c> (<c>src/cque.c:2444</c>): an object of the enactor's own.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string RestartingObjectFormat = "Restarting: {0}({1})";

		public const string HaltMustSpecifyPid = "You must specify a process ID.";
		public const string HaltInvalidPidFormat = "Invalid process ID format.";
		/// <summary>
		/// PennMUSH <c>process_command</c> (<c>src/game.c:1181</c>): a halted executor's command is
		/// refused and its owner told, once per refused command. Takes the dbref number alone, as
		/// Penn's <c>%d</c> does — never an objid.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectCommandRefusedFormat = "Attempt to execute command by halted object #{0}";

		public const string NotifyMustSpecifySemaphoreObject = "You must specify an object to use for the semaphore.";
		public const string NotifyMustSpecifyValidObjectAttribute = "You must specify a valid object with an optional valid attribute to use for the semaphore.";
		public const string NotifyMustSpecifyQregAssignments = "You must specify Q-register assignments.";
		public const string NotifyQregAssignmentsMustBePairs = "Q-register assignments must be in pairs: qreg,value[,qreg,value...]";
		public const string NotifyInvalidNumber = "Invalid number specified.";
		public const string NotifyNoTaskWaitingOnSemaphore = "No task is waiting on that semaphore.";

		public const string WaitCommandListMissing = "Command list missing";
		public const string WaitPermissionDenied = "Permission Denied.";
		public const string WaitInvalidTimeArgumentFormat = "Invalid time argument format";
		public const string WaitInvalidFirstArgumentFormat = "Invalid first argument format";
		public const string WaitInvalidPidSpecified = "Invalid PID specified.";
		public const string WaitWhatToDoWithProcess = "What do you want to do with the process?";
		public const string WaitInvalidTimeSpecified = "Invalid time specified.";

		public const string DrainInvalidNumber = "Invalid number specified.";
		public const string DrainCannotSpecifyBothAnyAndAttribute = "You may not specify both /any and a specific attribute.";
		public const string DrainCannotSpecifyBothAllAndNumber = "You may not specify both /all and a number.";

		public const string PsMustSpecifyPid = "You must specify a process ID.";
		public const string PsInvalidPidFormat = "Invalid process ID format.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsNoTaskWithPidFormat = "No task found with PID {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugTaskFormat = "@ps/debug: Task {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugOwnerFormat = "  Owner: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugSemaphoreFormat = "  Semaphore: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugCommandFormat = "  Command: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsDebugDelayFormat = "  Delay: {0}s";
		public const string PsSummaryHeader = "@ps/summary: Queue totals";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsCommandQueueFormat = "  Command queue: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsWaitQueueFormat = "  Wait queue: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsSemaphoreQueueFormat = "  Semaphore queue: {0}";
		public const string PsLoadAverageZero = "  Load average: 0.0, 0.0, 0.0";
		public const string PsQuickHeader = "@ps/quick: Your queue totals";
		public const string PsAllHeader = "@ps/all: All queued tasks";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsAllGroupFormat = "Group: {0} ({1} tasks)";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsQueueForTargetFormat = "@ps: Queue for {0}";
		public const string PsSemaphoreTasksHeader = "Semaphore tasks:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsSemaphoreTaskEntryFormat = "  [{0}] {1} ({2}): {3}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsAndMoreFormat = "  ... and {0} more";
		public const string PsWaitQueueHeader = "Wait queue tasks:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PsWaitTaskEntryFormat = "  [{0}] (delayed)";
		public const string PsQueueManagementNotImplemented = "Note: Queue management not yet implemented.";

		public const string TriggerMustSpecifyAttributePath = "You must specify an object/attribute to trigger.";
		public const string TriggerMustSpecifyObjectAttributePath = "You must specify an object/attribute path.";
		public const string TriggerPermissionDeniedDoNotControl = "Permission denied. You do not control that object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string TriggerNoSuchAttributeFormat = "No such attribute: {0}";
		/// <summary>PennMUSH <c>do_trigger</c> (<c>src/set.c:1345</c>).</summary>
		public const string TriggerTriggeredFormat = "{0} - Triggered.";
		public const string TriggerMustProvideMatchString = "You must provide a string to match when using /match.";
	}
}
