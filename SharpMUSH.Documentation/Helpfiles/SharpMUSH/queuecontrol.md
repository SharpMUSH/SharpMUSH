# @queue

`@queue/list [pid]`
`@queue/pause pid=reason`
`@queue/resume pid`
`@queue/list/owner player`
`@queue/pause/object object=reason`
`@queue/resume/owner player`

Inspect, pause or resume pending work without changing its PID or captured
registers. `/owner` selects the executor's owner; `/object` selects the actual
source executor. Both accept a current object name or reference. A bare numeric
selection is a PID. A pause reason is optional, plain text, at most 160
characters, with no control characters. Use @halt for cancellation.

Only pending delayed jobs and semaphore waiters can pause. Ready or running
commands cannot be suspended. Repeated pause or resume is harmless. Paused jobs
retain their queue reservations and continue to count against all limits.
Listings show state, remaining seconds, full source and owner identities, reason
and whether a semaphore signal has already arrived. They never show command
bodies or registers. Existing @ps displays a paused-job hint; @queue/list has the
full paused-state view.

Pausing freezes the remaining delay. A semaphore notification consumes its
permit even while paused, but its command does not execute until resumed. An
unsignalled waiter resumes its remaining timer. A signalled waiter enters the
normal serialized queue when resumed. Old timer callbacks cannot release a
replacement schedule. Every execution gets a fresh execution budget. Resume
rejects changed owner identities, recycled executors and missing or recycled
semaphore targets.

Existing `@wait/pid pid=seconds` also retimes delayed jobs and semaphore waits.
A leading `+` or `-` adjusts the remaining duration; `/until` takes absolute Unix
seconds. Retiming a paused job changes its frozen duration without resuming it.
Past deadlines become zero remaining time. Invalid or unrepresentable times are
rejected without changing the timer. These legacy operations retain their Penn
control and HALT-power permission rules.

The actual executing player must belong to an active account. Account roles do
not transfer to an owned thing, a caller or an enactor. Inspecting your queue
requires queue.inspect.own; changing it requires queue.control.own and normal
control of its current source. Inspecting or managing another owner's queue
requires the corresponding global queue.inspect or queue.control capability.
A global queue capability grants this queue operation; it does not grant object
editing rights. An explicit deny of an own scope remains effective even when
its parent global scope is granted.

On the first upgrade, existing built-in God and Wizard roles receive missing
administrative capabilities for snapshots, jobs, queues, profiling and reality.
Explicit permission settings and role assignments are preserved. This upgrade
runs once; removing a grant afterwards remains effective across restarts.

A direct PID operation needs the control scope. Bulk operations also need
inspection, process eligible entries in ascending PID order, and recheck control
for each entry. Listings and batches are limited to 200 entries. Narrow the
selection, or repeat a batch to handle remaining eligible entries. Permission
changes or cancellation can stop a batch after earlier entries have changed.
A hidden or removed PID returns NotFound without disclosing its metadata.

Pause state is process-local, like the pending command queue. Restart discards
both pending and paused work. It does not create a durable checkpoint; durable
recurring job definitions are a separate feature.
