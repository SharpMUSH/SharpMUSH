# queue budgets

Every admitted command occupies one slot until it finishes. Immediate commands,
@wait delays, semaphore waiters, and asynchronous attribute callbacks share the
same limits. Running work still occupies a slot. Cancellation retains a slot
until the immediate consumer removes the cancelled entry, preventing repeated
queue-and-cancel operations from retaining unlimited command bodies.

`global_queue_limit` defaults to 10000 admitted jobs. `player_queue_limit` defaults
to 100 and applies to the executor's owner, across that owner's objects and
connections. Wizards and executors with Queue power add the current database
object count (including garbage) to that owner allowance. The global ceiling
still applies. Before login, the connection handle supplies the owner bucket.
Reducing a limit does not discard existing work; new submissions are rejected
until usage falls below the limit. Capacity rejection never waits for room in
the queue, including when a running command submits another command.

A rejected submission has no PID. Game users receive a queue rejection notice;
service callers receive `QueueAdmissionResult` with a reason and no PID. Reasons
are global capacity, owner capacity, shutdown, and a missing executor. A delayed
or semaphore job retains its original reservation and PID when released. It
does not need to compete for capacity a second time. Immediate work is FIFO;
semaphore notifications and partial drains select ascending PIDs.

Wizards can use `@ps/all` to inspect admitted totals, configured limits, and
rejection counts by reason. Counts last for the lifetime of the engine process.
The configuration interface exposes both queue limits in the Limit category.

# execution budget

`queue_entry_cpu_time` is the legacy configuration name for an **elapsed-time**
limit in milliseconds. Its default remains 1000 milliseconds (one second).
Zero disables the deadline while keeping halt and shutdown cancellation active. It is not a measurement of
CPU consumed by the process or by a thread. The clock starts when execution
starts, so waiting on a delay or semaphore does not spend execution time.

Nested evaluation shares one monotonic deadline. Time spent waiting on HTTP or
SQL consumes the same budget. A separately queued job starts a fresh deadline.
Expiry returns `#-1 EXECUTION TIME LIMIT EXCEEDED`; it does not reset at an
attribute call or nested command list. Function invocation, recursion, output
size and regex ceilings remain independent limits.

Cancellation from halt or shutdown is distinct from deadline expiry.
Cancellation is cooperative: parser checkpoints stop further evaluation, and
HTTP/SQL operations receive the cancellation token. A database provider must
honor cancellation to interrupt an operation already inside that provider.
The scheduler never abandons an outstanding operation to run another command
concurrently. Regex operations retain their existing finite timeout and use the
remaining deadline when constructing a pattern near expiry.
