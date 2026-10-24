<!-- help-article
{
  "corpus": "help",
  "id": "mail-other",
  "lookup": "Other Mail Commands",
  "aliases": ["Mail-other", "@mail/clear", "@mail/unclear", "@mail/purge", "@mail/tag", "@mail/untag", "@mail/unread", "@mail/status"],
  "sections": [
    {"id": "tagging", "heading": "Tagging mail", "lookup": "other mail commands tagging"},
    {"id": "status", "heading": "Unread and status", "lookup": "other mail commands status"}
  ]
}
-->
# Other Mail Commands

`@mail/clear [<msg-list> | all]`<br>
`@mail/unclear [<msg-list> | all]`<br>
`@mail/purge`

`@mail/clear` marks mail in the current folder as cleared, and `@mail/unclear` takes the mark off. Cleared mail is kept, and can be uncleared, until you purge it. With no msg-list, every message in the current folder is cleared; with `all`, every message in every folder.

`@mail/purge` deletes all of your cleared mail, in every folder.

- [other mail commands tagging]: tagging mail to act on it en masse
- [other mail commands status]: marking mail unread, and setting its status

## Tagging mail

`@mail/tag [<msg-list> | all]`<br>
`@mail/untag [<msg-list> | all]`

These tag or untag mail in the current folder. Other commands then act on every tagged message when given `tagged` as the msg-list, and leave the tags on. With no msg-list, every message in the current folder is tagged or untagged; with `all`, every message in every folder.

To clear all mail from Paul and Chani:

```sharp
> @mail/tag *paul
> @mail/tag *chani
> @mail/clear tagged
> @mail/untag all
```

## Unread and status

`@mail/unread [<msg-list> | all]`<br>
`@mail/status [<msg-list> | all]=<status>`

`@mail/unread` marks messages you have read as new again.

`@mail/status` sets the status of the given messages. `<status>` is one of `tagged`, `untagged`, `cleared`, `uncleared`, `read`, `unread`, `urgent` or `unurgent`. `read` marks a new message read without showing it, `urgent` and `unurgent` set and clear the urgent flag, and the others do what @mail/tag, @mail/untag, @mail/clear, @mail/unclear and @mail/unread do.
