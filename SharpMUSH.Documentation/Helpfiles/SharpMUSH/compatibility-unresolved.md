<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-unresolved",
  "lookup": "compatibility unresolved",
  "aliases": [],
  "sections": [
    {
      "id": "command-scheduling-queued-or-inline-1132",
      "heading": "`$`-command scheduling: queued or inline (#1132)",
      "lookup": "compatibility unresolved command scheduling queued or inline 1132"
    },
    {
      "id": "connection-hooks-run-inline-1006-item-14",
      "heading": "Connection hooks run inline (#1006 item 14)",
      "lookup": "compatibility unresolved connection hooks run inline 1006 item 14"
    },
    {
      "id": "argument-count-error-wording",
      "heading": "Argument-count error wording",
      "lookup": "compatibility unresolved argument count error wording"
    },
    {
      "id": "halt-and-players-1006-items-1-and-2",
      "heading": "HALT and players (#1006 items 1 and 2)",
      "lookup": "compatibility unresolved halt and players 1006 items 1 and 2"
    },
    {
      "id": "empty-on-a-sticky-item-1006-item-4",
      "heading": "`EMPTY` on a `STICKY` item (#1006 item 4)",
      "lookup": "compatibility unresolved empty on a sticky item 1006 item 4"
    },
    {
      "id": "queue-fairness-between-owners-1006-item-15",
      "heading": "Queue fairness between owners (#1006 item 15)",
      "lookup": "compatibility unresolved queue fairness between owners 1006 item 15"
    },
    {
      "id": "regrabi",
      "heading": "`regrabi()`",
      "lookup": "compatibility unresolved regrabi"
    }
  ]
}
-->
# COMPATIBILITY UNRESOLVED

Known differences with **no decision recorded**. Do not write code that depends on either behaviour;
either may change.

## `$`-command scheduling: queued or inline (#1132)

**PennMUSH** queues a matched `$`-command's action and runs it from the queue.<br>
**SharpMUSH** executes the body inline, at the point of the match.<br>
**What follows from it.** Command ordering relative to other queued work, register isolation,
recursion depth and the effect of `@halt` and `@wait` can all differ.<br>
**Status.** #1132 records the source-path difference and asks for either the queued behaviour or an
explicit decision that inline dispatch is intended. Neither has happened. The paired transcripts that
would show the effects have not been captured.

Until it is settled, do not rely on a `$`-command body running before or after anything else queued
by the same line.

## Connection hooks run inline (#1006 item 14)

`ConnectionAnnounceService` evaluates connection hooks inline rather than admitting them to the
scheduler. Same shape of question as #1132, same lack of a decision.

## Argument-count error wording

PennMUSH writes four different messages depending on the function's declared range: `EXPECTS <n>`,
`EXPECTS <min> OR <max>`, `EXPECTS AT LEAST <min>`, `EXPECTS BETWEEN <min> AND <max>` (parse.c:2981).
SharpMUSH writes `EXPECTS AT LEAST <min>` or `EXPECTS AT MOST <max>`. Softcode that matches on the
text of an arity error will not port. No decision has been recorded on whether to adopt PennMUSH's
four forms.

## HALT and players (#1006 items 1 and 2)

**PennMUSH** queues an object's `@a`-action even when it is `HALT`ed, exempts players from the halted
check (`cque.c:530`), and sets `HALT` on any runaway, a player included (`cque.c:303-313`).<br>
**SharpMUSH** queues no action for a `HALT`ed object of any type, so that `@halt` stops an object
completely. As a consequence it never sets `HALT` on a runaway player, which would silence them.<br>
**Status.** The code records both as deliberate. #1006 asks whether to adopt PennMUSH's player
exemption, which would let a runaway player be halted too. The two change together.

## `EMPTY` on a `STICKY` item (#1006 item 4)

**PennMUSH**'s `do_empty` sends the *container* home in that case (`move.c:845-847`).<br>
**SharpMUSH** sends the item home.<br>
**Status.** #1006 asks for an explicit decision rather than copying PennMUSH's surprising behaviour.

## Queue fairness between owners (#1006 item 15)

Admission is bounded by global and per-owner limits, and a refused entry is reported with its reason.
Whether one owner's backlog should be able to delay another's, as it can in PennMUSH, is an open
design question.

## `regrabi()`

PennMUSH declares `REGRAB` and `REGRABALL` as taking up to four arguments and `REGRABI` as taking
three. SharpMUSH matches that exactly, including the inconsistency. Whether to give `regrabi()` the
fourth argument is undecided.
