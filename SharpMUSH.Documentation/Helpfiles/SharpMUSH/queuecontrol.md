<!-- help-article
{
  "corpus": "help",
  "id": "queue-control",
  "lookup": "@queue",
  "aliases": [],
  "sections": [
    {
      "id": "pause-state",
      "heading": "Pause state",
      "lookup": "@queue pause state"
    },
    {
      "id": "delay-and-notification-behavior",
      "heading": "Delay and notification behavior",
      "lookup": "@queue delay and notification behavior"
    },
    {
      "id": "retiming-jobs",
      "heading": "Retiming jobs",
      "lookup": "@queue retiming jobs"
    },
    {
      "id": "permissions",
      "heading": "Permissions",
      "lookup": "@queue permissions"
    },
    {
      "id": "bulk-operations",
      "heading": "Bulk operations",
      "lookup": "@queue bulk operations"
    },
    {
      "id": "restart-behavior",
      "heading": "Restart behavior",
      "lookup": "@queue restart behavior"
    }
  ]
}
-->
# @queue

`@queue/list [pid]`<br>
`@queue/pause pid=reason`<br>
`@queue/resume pid`<br>
`@queue/list/owner player`<br>
`@queue/pause/object object=reason`<br>
`@queue/resume/owner player`

Inspect, pause or resume pending work without changing its PID or captured
registers. `/owner` selects the executor's owner; `/object` selects the actual
source executor. Both accept a current object name or reference. A bare numeric
selection is a PID. A pause reason is optional, plain text, at most 160
characters, with no control characters. Use @halt for cancellation.

## Pause state

Only pending delayed jobs and semaphore waiters can pause. Ready or running
commands cannot be suspended. Repeated pause or resume is harmless. Paused jobs
retain their queue reservations and continue to count against all limits.
Listings show state, remaining seconds, full source and owner identities, reason
and whether a semaphore signal has already arrived. They never show command
bodies or registers. Existing @ps displays a paused-job hint; @queue/list has the
full paused-state view.

## Delay and notification behavior

Pausing freezes the remaining delay. A semaphore notification consumes its
permit even while paused, but its command does not execute until resumed. An
unsignalled waiter resumes its remaining timer. A signalled waiter enters the
normal serialized queue when resumed. Old timer callbacks cannot release a
replacement schedule. Every execution gets a fresh execution budget. Resume
rejects changed owner identities, recycled executors and missing or recycled
semaphore targets.

## Retiming jobs

Existing `@wait/pid pid=seconds` also retimes delayed jobs and semaphore waits.
A leading `+` or `-` adjusts the remaining duration; `/until` takes absolute Unix
seconds. Retiming a paused job changes its frozen duration without resuming it.
Past deadlines become zero remaining time. Invalid or unrepresentable times are
rejected without changing the timer. These legacy operations retain their Penn
control and HALT-power permission rules.

## Permissions

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

## Bulk operations

A direct PID operation needs the control scope. Bulk operations also need
inspection, process eligible entries in ascending PID order, and recheck control
for each entry. Listings and batches are limited to 200 entries. Narrow the
selection, or repeat a batch to handle remaining eligible entries. Permission
changes or cancellation can stop a batch after earlier entries have changed.
A hidden or removed PID returns NotFound without disclosing its metadata.

## Restart behavior

Pause state is process-local, like the pending command queue. Restart discards
both pending and paused work. It does not create a durable checkpoint; durable
recurring job definitions are a separate feature.
