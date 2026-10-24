<!-- help-article
{"corpus":"help","id":"application-drafts","lookup":"@appdraft","aliases":["appfields()","appfields","application drafts","@appdraft/show"],"sections":[{"id":"fields","heading":"Fields and answers","lookup":"@appdraft fields"},{"id":"callbacks","heading":"Callbacks and ownership","lookup":"@appdraft callbacks"},{"id":"lifetime","heading":"Lifetime and retries","lookup":"@appdraft lifetime"}]}
-->
# @appdraft

`@appdraft[/show] <object>/<declaration>=<action>[,<JSON fields>]`<br>
`appfields(<declaration>, <JSON fields>)`

@appdraft keeps a draft of a form, such as a job request, for the player filling it in, so a softcode system can let them fill it in a field at a time, check it, and file it once. The player keeps typing commands and chatting as usual while a draft is open; @appdraft never takes over their input, as @input does.

`<action>` is one of:

- `new`: start a draft, with any fields given.
- `set`: change the fields given.
- `review`: check the draft and mark it ready to file.
- `back`: return a reviewed draft to editing.
- `file`: file a reviewed draft.
- `cancel`: throw the draft away.

`<declaration>` is the attribute that describes the form, and `<JSON fields>` the values to set.

- [@appdraft fields]: declaring a form's fields, the answers, and appfields()
- [@appdraft callbacks]: the code that checks and files a draft, and who owns it
- [@appdraft lifetime]: how long a draft lasts, and filing once

## Fields and answers

`<declaration>` is an attribute holding a JSON array of the form's fields, up to 64. Each field is an object with a unique `key`, a `type` (`text`, the default, `textarea`, `select`, `boolean`, `number`, `date` as `YYYY-MM-DD`, `list` or `multiselect`), an optional `default`, and optional `validation`: `required` (true or false) and `max_length` (a number of characters). `<JSON fields>` is a JSON object of field values, each of its field's type; a field the declaration does not have is refused.

Each action answers with JSON, also left in the `appdraft_result` q-register: `{"ok":true,"fields":{...},"phase":"edit"}` (or `"review"`), or `{"ok":false,"errors":{...}}` naming each field that is wrong, `_global` for the draft as a whole.

appfields() makes the same checks of `<JSON fields>` against a declaration, without a draft, and returns `{"ok":true}` or the errors.

```sharp
&F me=[{"key":"title","type":"text","validation":{"required":true,"max_length":8}}]
think appfields(v(F),json(object))
```

Output: `{"ok":false,"errors":{"title":"title is required."}}`

## Callbacks and ownership

The draft belongs to the player at the connection that typed the command, or to the portal user of the form that sent it; the fields can never name another player. Each connection and each open form has drafts of its own.

Two attributes under the declaration do the system's own work, run as commands with the named arguments `%<draft_actor>` (the player), `%<draft_fields>` (the fields, as JSON) and `%<draft_action>`:

- `` <declaration>`VALIDATE `` runs on `review` and `file`. It checks what the structure cannot, such as whether the player may still file in that bucket, and sets `appdraft_result` to `{"ok":true}` or the errors.
- `` <declaration>`FILE `` runs on `file`, after VALIDATE passes, and does the filing. It sets `appdraft_result` to what the player is told, such as `{"ok":true,"message":"Filed job 12."}`.

The callbacks see their caller's arguments too, but hasarg() with `supplied` is 1 only for the three above; they get no %0-%9.

`/show` also shows the player the answer, through `` <declaration>`DISPLAY `` evaluated with the JSON as %0, at the connection that typed the command only, never their other connections. Without `/show`, the calling code decides how to show `appdraft_result`, in the game or as a web page.

## Lifetime and retries

A draft lasts 30 minutes from the last @appdraft that used it. Disconnecting, logging out or switching character ends the drafts of that connection. Drafts are kept in memory, so a restart loses them.

A draft must be reviewed before it is filed, and is filed once: sending `file` again with the same fields returns the first answer rather than filing twice, and with other fields is refused. A restart in the middle of filing can leave it unclear whether the filing happened, so check before filing again.

The jobs package uses @appdraft for `+request/draft new`, `+request/draft set title=<text>`, `review`, `back`, `file` and `cancel`. `+request` on its own still files in one step.
