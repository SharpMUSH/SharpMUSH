# Recurring jobs

A recurring job calls one attribute as the linked active player who creates it. It keeps
its ID, owning account, full player/target identities, attribute, description, timezone,
next firing, last firing attempt and last error in the provider-neutral world store.
World backups include these definitions. No external cron files need copying.

    @job/create #number:creation/ATTRIBUTE=0 9 * * 1-5|America/New_York|Weekday event
    @job/list
    @job/list/all
    @job/disable job-id
    @job/enable job-id
    @job/schedule job-id=*/15 * * * *|UTC
    @job/delete job-id

Use /all with administrator edits to select another account's job. The portal's Recurring
jobs page provides the same controls. jobs.manage.own permits your account's jobs;
jobs.manage permits other accounts' jobs. Both remain subject to the existing role
priority and explicit-denial rules. Creating a job always records your own active player;
editing another account's schedule never changes its execution identity.

The five fields are minute (0–59), hour (0–23), day of month (1–31), month (1–12), and
weekday (0–7, Sunday is 0 or 7). Each accepts *, comma lists, ascending ranges and /steps.
Examples: */15 means every fifteen units; 9-17 means 9 through 17; 1,15 means either value.
Names, seconds, ?, L, W, # and wraparound ranges are not accepted. If both day fields are
restricted (neither is the literal *), either may match. Matching both produces one firing.

Examples:

    0 9 * * 1-5       Weekdays at 09:00
    */15 * * * *     Every fifteen minutes
    0 0 1,15 * *     Midnight on the first and fifteenth
    0 12 15 * 0      Noon on Sundays or the fifteenth

Schedules use the named timezone's wall clock. A nonexistent daylight-saving time is
skipped. A repeated wall time fires once, at its earlier UTC occurrence. Changing timezone
or schedule computes a new future firing and invalidates any unstarted old callback.
Invalid schedules, unknown zones and schedules with no future date are rejected.

The engine records each firing claim before normal queue admission. Admission rejection
is recorded, with no retry of that firing. A delayed poll admits at most one current firing
per job and advances directly to a future occurrence. A firing is skipped while an earlier
firing owns a queue reservation, including canceled work waiting to drain. This prevents
a backlog of superseded callbacks. Restart retains definitions, clears interrupted
claims and skips past firings. Repeated startup initialization does not register duplicates.
There are at most 32 definitions per account and 256 per world.

Immediately before dispatch, the engine reloads the enabled definition and its original
account/player identities, rechecks the current jobs capability, normal control of the
target and execution access to the exact local attribute. Unlinking/disabling the account,
revoking its role, destroying/recycling the player or target, deleting the attribute,
disabling/deleting the job, or changing its schedule prevents old callbacks from executing.
The attribute runs through the normal serialized queue with a fresh execution budget;
no submitting command budget or captured role claims are retained. A command already
running finishes under its existing budget. Definitions do not bypass Penn game powers.

The engine is the single writer of these definitions. Last firing attempt means the time
of admission attempt; rejected and failed attempts remain visible. Attribute errors and
budget failures are recorded without exposing provider internals.
