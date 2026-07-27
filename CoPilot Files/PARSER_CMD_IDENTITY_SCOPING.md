# Follow-up scope: real `%c` / `%u` (command identity through the queue)

**Status:** not started. `%u` currently aliases `%c` (both return the raw command); documented as a known limitation in `help pennmush-compatibility`. This is P3 item #11 from the parser-parity review, deferred as its own feature.

**Why this is a feature, not a fix:** `%c` (raw command) and `%u` (evaluated command) track *the command the current queue entry is executing*. PennMUSH threads two strings — `cmd_raw` and `cmd_evaled` — through the queue and command pipeline and inherits them into triggered/attribute contexts. SharpMUSH has only a single `ParserState.Command` (the raw text) and no evaluated-command string at all. Producing `%u` correctly means replicating that lifecycle, including the inheritance rules — not reusing any single already-computed value. A naive "evaluate the command line for `%u`" would double-evaluate every command and duplicate side effects.

## Read this first: verify the exact semantics with the oracle

My initial model ("`%u` = the evaluated trigger line") was **wrong** — the PennMUSH oracle disproved it. Before implementing, pin the real behavior with `SharpMUSH.Tests/PennMUSH/test_parser_parity.t` (build recipe in that file). One measured result to explain and reproduce:

```text
&TU me=$tu *:@pemit me=RAW=[%c]__EVAL=[%u]
tu [add(1,2)]
  -> RAW=@pemit me=RAW=[%c]__EVAL=[%u]__EVAL=
```

That is: inside the `$`-command's `@pemit` action, `%c` was the **`@pemit` action line itself** (not the trigger `tu [add(1,2)]`), and `%u` was **empty**. The action was queued as its own entry, so its `cmd_raw` was re-stamped at dequeue. Do not implement until a batch of oracle cases (direct command, `$`-command action, `@dolist`/`@switch` body, `@force`, inline vs queued) makes the raw-vs-evaluated values and the inheritance unambiguous. The semantics are subtle enough that guessing will be wrong.

## PennMUSH lifecycle (source of truth)

All in `pennmush/src`, commit `80a1d5b9`:

1. **Init:** `parse.c:1893-1894` — a fresh `pe_info` has `cmd_raw = cmd_evaled = NULL`.
2. **Queue dequeue:** `cque.c:1154-1163` — for each `;`-split command in the action list, `process_expression(..., PE_NOTHING, ...)` extracts the command text into `rbuff`, then `pe_info->cmd_raw = strdup(rbuff)`. So `cmd_raw` is re-stamped *per command executed*, to the raw (unevaluated, but `;`/braces-resolved) command line.
3. **Command parse:** `command.c:1499-1502` — `pe_info->cmd_evaled = strdup(commandraw)`, where `commandraw` is the command reassembled from its **evaluated** pieces (canonical command name + evaluated switches + evaluated `=`/comma args), built up through `command_parse`. Also `command.c:1566-1567` sets both for one path.
4. **Attribute / `$`-command execution:** `attrib.c:1899-1910` (`atr_comm_match`) — the new `pe_info` **inherits** `cmd_raw`/`cmd_evaled` from the triggering queue entry (`from_queue->pe_info`) when present and non-empty; otherwise both are set to the attribute action text `str`. This is why a `$`-command body sees the *triggering* command's identity — until its own action is queued and re-stamped per step 2.
5. **Nested `pe_info`:** `parse.c:1980-1987` — copied into a child `pe_info` only when the `PE_INFO_COPY_CMDS` flag is set.
6. **Special case:** `look.c:466-467` — the implicit LOOK sets both to `"LOOK"`.

Net: `%c`/`%u` = the raw / evaluated form of the command the innermost **queue entry** is running, with inheritance into inline (non-queued) attribute evaluation.

## SharpMUSH mapping

| PennMUSH | SharpMUSH today | Gap |
|---|---|---|
| `pe_info->cmd_raw` | `ParserState.Command` (raw text, set in `CommandParse`) | roughly present |
| `pe_info->cmd_evaled` | *nothing* | must be added |
| set per `;`-split at dequeue (`cque.c`) | `CommandListParse` visits each command; `Command` set once at top-level `CommandParse` | not re-stamped per command in a list |
| `cmd_evaled = commandraw` (reassembled evaluated command) | no reassembly — built-ins read args directly, never build an evaluated command string | must be produced |
| inherit into `$`-command / attribute exec (`attrib.c`) | `HandleUserDefinedCommand` / `HandleUserDefinedCommandInline` push state with `Command` = raw, no inheritance of an evaluated form | must add inheritance |
| read from current `pe_info` | `Substitutions.cs:83-84` reads `parser.StateHistory(2).Command` for **both** `%c` and `%u` | fragile 2-levels-up guess; should read the current state |

Key files: `SharpMUSH.Library/ParserInterfaces/ParserState.cs` (the record + `Command`), `SharpMUSH.Implementation/MUSHCodeParser.cs` (`CommandParse`, `CommandListParse`, `StateHistory`), `SharpMUSH.Implementation/Visitors/SharpMUSHParserVisitor.cs` (`EvaluateCommands`, `HandleInternalCommandPattern`, `HandleUserDefinedCommand[Inline]`), `SharpMUSH.Implementation/Substitutions/Substitutions.cs:83-84` (`%c`/`%u`), `SharpMUSH.Library/Services/TaskScheduler.cs` (the queue).

## Proposed shape

1. **State.** Add `MString? CommandEvaluated` to `ParserState` (trailing, defaulted — the ~13 construction sites use named args, so they're unaffected; `with` carries it). Optionally rename the existing `Command` to `CommandRaw` for clarity, or leave it.
2. **Raw.** Stamp `CommandRaw` per `;`-split command as it begins executing — in `VisitCommand`/`EvaluateCommands`, not only once at `CommandParse`. Mirrors `cque.c:1163`.
3. **Evaluated.** Produce the evaluated command string. This is the real work:
   - For built-ins, reassemble it from the evaluated switches + args that `ArgumentSplit` already computes (it evaluates them once — capture the reassembly there, do **not** re-evaluate).
   - For `$`-commands, the `#2` fix already computes `evaluatedCommandText`; reuse it for the triggering line, then let the queued action re-stamp per step 2.
   - The hard invariant: **never evaluate a second time for `%u`** — capture from the single evaluation the dispatch already performs, or side effects double.
4. **Inheritance.** In `HandleUserDefinedCommand[Inline]` and any attribute-execution entry, inherit `CommandRaw`/`CommandEvaluated` from the triggering state when the action runs inline; when it's queued as its own entry, let it be re-stamped. Mirror `attrib.c:1899-1910` exactly (including the "empty → use the attribute text" fallback).
5. **Reads.** Change `Substitutions.cs` so `%c` → `CommandRaw` and `%u` → `CommandEvaluated`, reading from the appropriate state (fix the `StateHistory(2)` fragility — prefer stamping the current state at the command boundary and reading `CurrentState`).

## Risks / correctness surface

- **Double evaluation** is the trap (same one that sank the P3 #5 "raise MaxArgs" and absorb attempts). The evaluated command must be *captured from* the dispatch's existing evaluation, never a fresh one.
- **Inheritance rules** are the subtle part — inline attribute eval inherits; a queued action re-stamps. Get this wrong and `%c`/`%u` report the wrong command in nested/queued contexts.
- **`StateHistory(2)`** is already a fragile assumption about stack depth; the rewrite should remove it, which itself needs regression coverage (any current `%c` behavior that leans on it).
- **Per-`;`-command re-stamping** in a command list must not disturb the existing `DirectInput`/command-list semantics.

## Test plan

- Extend `test_parser_parity.t` with a `%c`/`%u` block covering: a direct built-in with a function arg, a `$`-command action, an `@dolist`/`@switch` body, `@force`, and inline vs queued attribute execution — capturing PennMUSH's exact raw/evaluated values (they are not obvious; see the measured case above).
- Mirror each in a SharpMUSH TUnit test, and prove discrimination by reverting the change.
- Full suite green; watch `SubstitutionUnitTests`/command tests for any reliance on the old `StateHistory(2)` `%c` behavior. (Note: `SubstitutionUnitTests` asserts via `NotifyService.Notify(...)` **without** `.Received()`, so it verifies nothing — use `FunctionParse`-and-assert for real coverage.)
