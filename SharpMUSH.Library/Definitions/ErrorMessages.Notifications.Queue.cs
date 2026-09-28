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
		public const string QueueUsage = "Admitted jobs: {0}; global limit: {1}; per-owner limit: {2}.";
		public const string QueueRejections = "Queue rejections ({0}): {1}.";
		public const string Halted = "Halted.";

		public const string HaltMustSpecifyPid = "You must specify a process ID.";
		public const string HaltInvalidPidFormat = "Invalid process ID format.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltTaskHaltedFormat = "Task {0} halted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltNoTaskWithPidFormat = "No task found with PID {0}.";
		public const string HaltMustSpecifyTarget = "You must specify a target object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedPlayerAndObjectsFormat = "Halted {0} and all their objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectWithActionsFormat = "Halted {0} with replacement actions.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HaltedObjectFormat = "Halted {0}.";
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
		public const string TriggerMustProvideMatchString = "You must provide a string to match when using /match.";
	}
}
