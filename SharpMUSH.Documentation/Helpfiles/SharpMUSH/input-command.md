<!-- help-article
{
  "corpus": "help",
  "id": "input-command",
  "lookup": "@input",
  "aliases": [],
  "sections": [
    {"id": "start", "heading": "Starting a session", "lookup": "@input start", "aliases": ["@input/start"]},
    {"id": "patterns", "heading": "Patterns and captures", "lookup": "@input patterns", "aliases": ["@input/wild", "@input/regex"]},
    {"id": "typed-text", "heading": "Typed text", "lookup": "@input typed text"},
    {"id": "prompts", "heading": "Prompting, replacing and cancelling", "lookup": "@input prompts", "aliases": ["@input/prompt", "@input/cancel"]},
    {"id": "timeout", "heading": "Timeout", "lookup": "@input timeout"},
    {"id": "rescue", "heading": "Rescuing a stuck player", "lookup": "@input rescue", "aliases": ["@input/rescue"]},
    {"id": "limits", "heading": "Checks and limits", "lookup": "@input limits"},
    {"id": "example", "heading": "Example", "lookup": "@input example"},
    {"id": "evaluating", "heading": "Evaluating typed text", "lookup": "@input evaluating"}
  ]
}
-->
# @input

`@input/start[/wild|/regex] <prompt>=<exit pattern>,<exit attribute>[,<pattern>,<attribute>...][,<timeout-seconds>]`<br>
`@input/prompt <prompt>`<br>
`@input/cancel`<br>
`@input/rescue <player>`

Starts a guided input session on the current Telnet or WebSocket connection, shows the player `<prompt>`, and sends each line they type to an attribute picked by pattern.

**Warning:** while a session is open, every line the player sends goes to your attributes, and nothing they type runs as a command, so a player can write plain text without setting off other commands. The exit pattern is the player's way out: name it in the first prompt and in every later one, and pick one nobody types as ordinary text (`.done` rather than `done`). Do not rely on the timeout to end a session: it fires a fixed time after the start, even while the player is typing. The built-in `@input/cancel` escape ends the session without running anything, so whatever the exit attribute saves is never saved.

- [@input start]: pattern and attribute pairs, and refusals
- [@input patterns]: how patterns match, and what they capture
- [@input typed text]: what an attribute receives, and as whom it runs
- [@input prompts]: prompting again, replacing and cancelling
- [@input timeout]: what happens when the time runs out
- [@input rescue]: ending a stuck player's session, for staff
- [@input limits]: the checks on every line, and session limits
- [@input example]: a notepad that takes a note of many lines
- [@input evaluating]: evaluating typed text on purpose

## Starting a session

`@input/start[/wild|/regex] <prompt>=<exit pattern>,<exit attribute>[,<pattern>,<attribute>...][,<timeout-seconds>]`

The connection must be logged in to a character, and that character must be the command's enactor. There is no handle or player selector. Omitting `/start` also starts a session.

After the prompt come pattern and attribute pairs, up to ten, then an optional timeout. Each attribute is `<attribute>` on the executor or `<object>/<attribute>`; the executor must control that object and have read and execute access to the attribute, which must be stored directly on it. Patterns are tried in order and the first match runs its attribute:

- The first pair is the exit, and it is required. A line matching it ends the session, and then its attribute runs; that is where the work is saved, or the player told goodbye. It is also what runs when the time runs out.
- Every later pair keeps the session open.
- A pattern of `*` takes any line no other pattern matched.
- A line nothing matches runs nothing. The player is told the exit pattern, and the session stays open.

The command is refused with `#-1 INPUT NEEDS AN EXIT PATTERN AND ATTRIBUTE` without a first pair, `#-1 INPUT PATTERNS AND ATTRIBUTES COME IN PAIRS` when a pattern has no attribute, and `#-1 INVALID INPUT PATTERN` for a pattern that does not compile. `@input/cancel` cannot be the exit pattern (`#-1 INPUT EXIT PATTERN CANNOT BE @INPUT/CANCEL`), since that line always leaves without running anything.

## Patterns and captures

Patterns match the whole line, with spaces around it trimmed and case ignored. With `/wild` they are wildcard patterns and with `/regex` regular expressions, and what they capture is passed as in a `$`-command: wildcards from `%0`; for a regular expression, the whole match in `%0` and its groups from `%1`. An exact pattern or `*` passes the line in `%0` exactly as typed. The named argument `reason`, read with `r(reason,args)`, is `exit` for the exit pattern, `input` for any other, and `timeout` when the time ran out. Like a regular expression's named group it lasts only for that run, so nothing has to clear it; it replaces a group named `reason`.

## Typed text

An attribute gets the line and nothing else: brackets, semicolons, percent substitutions, and newlines are literal data, not commands or expressions. Empty input and whitespace are lines too. A transport that sends a paste as separate lines sends each line, and capture continues until the exit, so later pasted lines do not become commands.

Each attribute runs as the initiating executor, with the connected character as enactor. `%@` is kept from the code that ran `@input/start`: from a $-command, that is the player who typed it. It gets fresh registers and evaluation limits on each line. A line uses the normal admitted input entry and its execution budget. Queue rejection consumes no execution capacity; it never turns captured input into ordinary commands. A timeout run also requires normal queue admission.

## Prompting, replacing and cancelling

`@input/prompt <prompt>`<br>
`@input/cancel`

From an attribute, use `@input/prompt` to send another prompt to this connection, or `@input/start` to replace the session; a replacement may name different patterns. A new session invalidates queued responses to the former session. Replies waiting behind session startup, including blank replies, belong to the first session opened after they were queued. Replacing that session discards its remaining queued replies, including queued cancel messages. If that session ends before they execute, they are discarded rather than run as commands. `@input/cancel` ends capture without running anything and restores ordinary commands. Cancellation prevents attributes already running on this connection from reopening or managing capture; a later independent start is still allowed. A player can always leave by sending exactly `@input/cancel` (case-insensitive) as a complete input message. This escape is checked before queue admission, even when the queue is full. Extra spaces or appended commands make it ordinary literal session data.

## Timeout

The timeout is a safety net for a player who walked away. It defaults to 60 seconds and may be 1-3600 seconds. It is measured from session start and is not extended by input or prompts. At expiry, capture ends and the exit attribute runs with empty `%0` and `timeout` as `r(reason,args)`. Sending the cancel escape after expiry does not suppress an already pending timeout run. Starting another session is refused while the expired generation still owes its timeout run; the exit attribute itself may start the next session. An attribute is skipped if its binding or authority has changed or admission fails. Disconnect, logout, character switch, replacement, and engine restart end capture without running anything. A halted executor, an unhandled failure, or an exhausted execution budget also ends capture.

## Rescuing a stuck player

`@input/rescue <player>`

`@input/rescue <player>` is for staff with the `players.moderate` permission. It ends every open session on that player's connections as if its time had run out: capture stops at once, the player is told a staff member ended it, and each exit attribute runs with empty `%0` and `timeout` as `r(reason,args)`, within about a second. Use it when a player is stuck in a session whose code has no way out. The command returns how many sessions it ended.

## Checks and limits

Every delivery rechecks the connection incarnation, full character and object identities, ownership, control, and attribute access. These checks share the line's execution budget. Prompts are bound to the original connection and are discarded if that connection is replaced before delivery. Changed ownership, deleted/recycled objects, or revoked permission end the session safely. Two connections playing the same character have independent sessions and cannot consume one another's input. There may be at most 1024 sessions globally, 64 per initiating owner, and one per connection. An input message may contain at most 65,536 UTF-16 code units; longer input is rejected while capture remains active.

## Example

This example is a notepad with a `+note` command that takes a note of any number of lines. A line of just `.done` ends the session and runs `` INPUT`NOTE`DONE ``, which saves the note; `.undo` runs `` INPUT`NOTE`UNDO ``, which drops the last line; any other line goes to `` INPUT`NOTE`LINE ``, which adds it. If the time runs out, `` INPUT`NOTE`DONE `` still saves what was written:

```sharp
@create Notepad
@set Notepad=!no_command
&CMD`NOTE Notepad=$+note:&DATA`DRAFT me; @input/start Type your note. Send .done on a line by itself to save it.=.done,INPUT`NOTE`DONE,.undo,INPUT`NOTE`UNDO,*,INPUT`NOTE`LINE,1800
&INPUT`NOTE`LINE Notepad=&DATA`DRAFT me=[v(DATA`DRAFT)][if(hasattr(me,DATA`DRAFT),%r)]%0; @input/prompt Next line, .undo to drop the last, or .done to save:
&INPUT`NOTE`UNDO Notepad=&DATA`DRAFT me=[ldelete(v(DATA`DRAFT),words(v(DATA`DRAFT),%r),%r)]; @input/prompt Dropped. Next line, or .done to save:
&INPUT`NOTE`DONE Notepad=&DATA`NOTE me=v(DATA`DRAFT); @pemit %#=[if(strmatch(r(reason,args),timeout),Time ran out%, so your note was saved as it was.,Note saved.)]
```

Output: a player carrying the notepad types `+note`, then `Hello [there]`, `oops`, `.undo`, `look` and `.done`. `` get(Notepad/DATA`NOTE) `` returns the two lines `Hello [there]` and `look`; none was evaluated or run. The player sees `Note saved.` and is back at ordinary commands.

## Evaluating typed text

Typed text is never evaluated: storing it, or substituting `%0` or a register holding it, keeps it exactly as typed. To evaluate it on purpose, do it once, as the player who typed it, with `objevals()`; never as the object, which may belong to someone with far more power. Do it in the exit attribute, over the whole text, or in the `*` attribute, a line at a time, to show the player each result as they type. `objeval()` will not do: it evaluates its expression once, which only substitutes the register and gives the typed text back as it was. In the notepad, this exit attribute evaluates the note as the player before saving it:

```sharp
&INPUT`NOTE`DONE Notepad=&DATA`NOTE me=[objevals(%#,v(DATA`DRAFT))]; @pemit %#=Note saved.
```

Output: a note typed as `[add(1,2)] for %n` is saved as `3 for` and the player's name. `objevals()` evaluates as the player only when the object controls them, as a wizard-owned object does; otherwise it evaluates as the object itself, so evaluate typed text only from an object that controls its players.

::: seealso
- [@prompt]
- [@trigger]
- [@include]
:::
