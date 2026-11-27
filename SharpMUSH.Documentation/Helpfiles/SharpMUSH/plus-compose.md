<!-- help-article
{
  "corpus": "help",
  "id": "plus-compose",
  "lookup": "+compose",
  "aliases": [],
  "sections": [
    {"id": "starting", "heading": "Starting a message", "lookup": "+compose starting"},
    {"id": "writing", "heading": "Writing and editing", "lookup": "+compose writing"},
    {"id": "sending", "heading": "Sending", "lookup": "+compose sending"},
    {"id": "drafts", "heading": "Stopping and going on later", "lookup": "+compose drafts"}
  ]
}
-->
# +compose

`+compose [<players>[=<subject>]]`

Writes a mail message a line at a time, from any client. The game asks who it is to and the subject, then takes the message one line at a time until you send it. The optional bundled `mail-composer` package adds this command and implements it in softcode over [@input]; staff install it from Admin, Packages, or tick it when they set up the game.

While you are writing, every line you type goes to the message, not to the game: a line that looks like a command is a line of the message. Commands to the composer start with a dot.

- [+compose starting]: the prompts, and skipping them
- [+compose writing]: the dot commands, and editing a line
- [+compose sending]: what readers get
- [+compose drafts]: stopping, and going on with a draft later

## Starting a message

`+compose` on its own asks for both in turn:

| Prompt | What to type |
|--------|--------------|
| To | One or more players. |
| Subject | The subject, at most 60 characters. |

Separate players with commas or spaces. A part between commas that is one player's whole name is that player, so `Alice, Carol Ann` needs no quotes. A dbref, `me` and a mail alias such as `+crew` work too. A name that finds nobody is named back to you and the prompt is asked again.

`+compose <players>` fills in To and asks only for the subject. `+compose <players>=<subject>` goes straight to the message.

```
> +compose Alice=Plans for Friday
[MAIL] Write your message a line at a time. .send sends it, .proof shows it, .help lists the rest.
```

## Writing and editing

Each line you type is the next line of the message, exactly as typed: nothing in it is evaluated, and a blank line is a blank line, as is a line of only `.`, for a client that does not send an empty line. A line starting with two dots adds a line starting with one, so `..send` adds the line `.send`.

| Command | Action |
|---------|--------|
| `.send` | Send the message. |
| `.proof` | Show the message, its lines numbered. |
| `.edit <n> <text>` | Replace line `<n>`. |
| `.insert <n> <text>` | Put a line before line `<n>`. |
| `.delete <n>` | Take out line `<n>`. |
| `.to <players>` | Change who it is to. |
| `.subject <text>` | Change the subject. |
| `.urgent` | Send it urgent, or not. |
| `.quit` | Stop, and keep the draft for later. |
| `.abort` | Stop, and throw the draft away. |
| `.help` | List these commands. |

`.to` and `.subject` work at the To and Subject prompts too. Any other line starting with a dot and a word, such as `.sned`, is refused and not added.

## Sending

`.send` sends the message as `@mail` would, from you: your [@mailsignature] is added, each recipient's mail lock decides whether they take it, and `@mail` reports each one it reached or why it could not. A player named more than once, or named and also in a +alias you name, gets the message once.

```
> .send
MAIL: You sent your message to Alice.
MAIL: You sent your message to Carol Ann.
```

Output: Alice and Carol Ann each get the message from you, as you wrote it.

The message must have a To and at least one line that is not blank; otherwise the composer says what is missing and stays open. If nobody took the message, it says `Not sent.` and stays open with the draft as it was. Once anyone has it, the draft is gone.

## Stopping and going on later

A character has one draft, kept on the character until it is sent or thrown away.

- `.quit` stops and keeps the draft. `+compose` on its own goes on with it, at the prompt where you stopped.
- `.abort` throws the draft away.
- Typing exactly `@input/cancel` stops the composer as it stops any [@input] session, and keeps the draft.
- Disconnecting keeps the draft. So does the end of the hour an [@input] session lasts, and a staff member's `@input/rescue`.

While a draft is kept, `+compose <players>` is refused, so a draft is never replaced by accident. Go on with it, then send it or `.abort` it.

::: seealso
- [@mail]
- [@input]
- [compatibility mail]
:::
