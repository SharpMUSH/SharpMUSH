# Bounded queued work

Ordinary `@trigger` admits one new action entry and returns before its body runs. The submitting list finishes first. Use `@trigger/inline` when the caller needs an immediate result, and `@include` when code should run as the current executor. An independent entry gets copied arguments and q-registers and fresh invocation/depth counters; its queue admission still consumes the existing owner/global capacity, and its execution still has an elapsed-time limit.

For a finite list, ordinary `@dolist` yields promptly but admits one entry per item. `@dolist/inline` admits no children, but one actor occupies the consumer until that block finishes. A small inline page followed by one ordinary trigger bounds pending work while giving another actor a turn between pages.

## A supported paging pattern

Use a dedicated worker object that you control, with one job active at a time. This worked example processes the immutable sequence 1 through 80, in pages of eight. Each row writes a deterministic result and only then advances the completion count. Enter these attributes as ordinary client commands, replacing `<worker>` with its dbref:

```sharp
&DONE <worker>=0
&STATUS <worker>=idle
&ROW <worker>=&ROW`%0 me=%0|[words(iter(lnum(60),add(%i0,1)))];&DONE me=inc(get(me/DONE))
&PAGE <worker>=@dolist/inline lnum(%0,min(add(%0,7),80))={@include me/ROW=##};@assert lt(get(me/DONE),80)={&STATUS me=complete};@trigger me/PAGE=add(%0,8)
&START <worker>=@assert not(strmatch(get(me/STATUS),running));&DONE me=0;&STATUS me=running;@trigger me/PAGE=1
@trigger <worker>/START
```

Read `get(<worker>/DONE)` and `get(<worker>/STATUS)` to inspect progress. `complete` is written only after all 80 inline row calls finish. The last page does not admit a continuation. The worker has at most one waiting continuation; during admission it may also have the current page running, so its admitted count can momentarily be two.

For real records, capture a stable ordered set of IDs and an upper bound before starting, or explicitly define how concurrent inserts/deletes affect traversal. Do not paginate a changing unordered search and assume it represents a snapshot. Validate a row's successful effects before incrementing `DONE`; a normal softcode error or failed operation is not necessarily an evaluation-limit exception.

`@halt <worker>` cancels pending continuations. A quota refusal also stops further production. Neither means the unfinished rows completed: `STATUS` remains `running` with `DONE < 80`. Treat that combination with no queued continuation as interrupted/stalled, inspect the queue diagnostics, and decide explicitly whether to restart. Repeated `START` calls are refused while it is running. Only reset the status after stopping the previous job. The queue is not a durable job journal; process failure requires application-specific recovery. This example's deterministic row writes are safe to repeat, but arbitrary side effects may not be.

An oversized row cannot be made safe merely by paging. A function/depth/output limit stops the current command list, including its literal completion tail; an elapsed execution limit also stops the entry. A fresh sibling entry can still run. Choose page size from the cost of the actual row, not only its count.

## What the measurement establishes

`QueueContractRuntimeTests.MeasureTwoActorBoundedProduction` uses the real scheduler, parser and embedded database. It compares 80 queued rows, one 80-row inline block and eight-row pages. Each row evaluates 60 `ADD` calls through `ITER`, writes its result, and increments a completion attribute. An independently owned second actor runs a real command behind the producer. A gate fixes their admission order; parser/storage paths are warmed first.

The second actor sees exactly 80 pending row entries for ordinary dolist, zero for inline, and one pending continuation for paging. Inline can have the highest throughput while imposing a long second-actor delay; paging trades some delay for bounded pending work. The test checks all 80 result values and completion count, then verifies no remaining worker entries. Separate tests halt or refuse the next page after eight completed rows and verify that no completion is claimed.

Wall-clock samples depend on the machine, storage, build and other work. Peak admission is sampled every millisecond and includes running entries; the pending count at the second actor's turn is exact. These observations establish a bounded-work pattern and its tradeoff, not a latency guarantee or a claim that paging is always faster. The [queue acceptance record](../superpowers/plans/2026-10-16-queue-contracts.md) contains the tested settings and final sample values.

## Compatibility notes

Code that relied on ordinary `@trigger` finishing before the next statement must opt into inline execution or use an explicit queued continuation. `/inplace` is the inline/localize/nobreak alias. Ordinary triggered attributes begin without the caller's old loop/switch/regexp frames; their explicit arguments and q-registers are copied. Generic queued text such as `@wait` and queued switch actions retains the detached contexts its deferred text needs.

HTTP and SQL callbacks capture caller state when admitted. Later caller register mutations no longer change their input, and HTTP response q-registers no longer write into a completed caller's frame. Named/numbered aliases and `hasarg(...,supplied)` retain the argument-contract rules.
