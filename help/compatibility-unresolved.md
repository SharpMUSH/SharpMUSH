<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-unresolved",
  "lookup": "compatibility unresolved",
  "aliases": [],
  "sections": [
    {
      "id": "empty-on-a-sticky-item",
      "heading": "`EMPTY` on a `STICKY` item",
      "lookup": "compatibility unresolved empty on a sticky item"
    },
    {
      "id": "queue-fairness-between-owners",
      "heading": "Queue fairness between owners",
      "lookup": "compatibility unresolved queue fairness between owners"
    },
    {
      "id": "regrabi",
      "heading": "`regrabi()`",
      "lookup": "compatibility unresolved regrabi"
    }
  ]
}
-->
# Compatibility Unresolved

Known differences with **no decision recorded**. Do not write code that depends on either behaviour; either may change.

## `EMPTY` on a `STICKY` item

**PennMUSH**'s `do_empty` sends the *container* home in that case (`move.c:845-847`).<br>
**SharpMUSH** sends the item home.<br>
**Status.** No decision yet on whether to copy PennMUSH's behaviour, which is surprising, or keep this one as a choice.

## Queue fairness between owners

Admission is bounded by global and per-owner limits, and a refused entry is reported with its reason. Whether one owner's backlog should be able to delay another's, as it can in PennMUSH, is an open design question.

## `regrabi()`

PennMUSH declares `REGRAB` and `REGRABALL` as taking up to four arguments and `REGRABI` as taking three. SharpMUSH matches that exactly, including the inconsistency. Whether to give `regrabi()` the fourth argument is undecided.
