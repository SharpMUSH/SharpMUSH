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

An attribute gets the line and nothing else: brackets, semicolons, percent signs and line breaks in it are plain text, never commands or code. An empty line, or one of only spaces, is a line too. A client that sends a paste as separate lines sends each line to the session, until the exit, so no pasted line runs as a command.

Each attribute runs as the object that started the session, with the connected character as the enactor (%#). `%@` is whatever it was for the code that ran `@input/start`: from a `$`-command, the player who typed it. Each line starts with fresh q-registers and its own evaluation limits, and is queued as typed input is. When the queue is full, the line is dropped, never run as a command. The timeout's run is queued the same way.

## Prompting, replacing and cancelling

`@input/prompt <prompt>`<br>
`@input/cancel`

From an attribute, `@input/prompt` sends another prompt to this connection, and `@input/start` replaces the session with a new one, which may have other patterns. Lines the player typed for the old session that have not run yet are thrown away, never run as commands; lines typed while a session was starting go to it.

`@input/cancel` ends the session without running anything, and the player's lines are commands again. After it, an attribute still running from that session cannot reopen it, though other code can start a new one.

A player can always leave by typing exactly `@input/cancel`, in any case, as a whole line. It works even when the queue is full. With anything else on the line, such as extra spaces or another command, it is an ordinary line of the session.

## Timeout

The timeout is a safety net for a player who walked away. It is 60 seconds unless given, and can be 1 to 3600. It counts from when the session started: typing and prompts do not extend it. When it runs out, the session ends and the exit attribute runs with an empty `%0` and `timeout` as `r(reason,args)`; typing `@input/cancel` after that does not stop that run. A new session cannot start until the run has happened, except from the exit attribute itself.

The exit attribute is skipped if the player's connection or character, or the object's control of the attribute, has changed since the session started. Disconnecting, logging out, switching character, a replacement session and a server restart end the session without running anything, as do a halted object, an error in an attribute, and running out of evaluation limits.

## Rescuing a stuck player

`@input/rescue <player>`

`@input/rescue <player>` is for staff with the `players.moderate` permission. It ends every open session on that player's connections as if its time had run out: capture stops at once, the player is told a staff member ended it, and each exit attribute runs with empty `%0` and `timeout` as `r(reason,args)`, within about a second. Use it when a player is stuck in a session whose code has no way out. The command returns how many sessions it ended.

## Checks and limits

Before each line runs, the server checks again that it is the same connection and character, and that the object still exists, has the same owner, and controls and may run the attribute. If any of that changed, the session ends without running anything. A prompt for a connection that has since been replaced is not sent.

Two connections playing the same character have sessions of their own, and one never gets the other's lines. There can be at most one session per connection, 64 started by objects of one owner, and 1024 in the game. A line longer than 65,536 characters is refused, and the session stays open.

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
