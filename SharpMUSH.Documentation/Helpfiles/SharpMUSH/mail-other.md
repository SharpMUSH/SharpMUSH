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

- `@mail/clear [<msg-list> | all]`
- `@mail/unclear [<msg-list> | all]`
* These commands mark mail in the current folder as cleared or uncleared. Cleared mail is kept, and can be uncleared, until you purge it.
* If no msg-list is specified, all mail in your current folder is cleared.
* If "all" is given instead of a msg-list, all mail in **all** folders is cleared/uncleared.

- `@mail/purge`
* Deletes all of your cleared mail, in every folder.

- [other mail commands tagging]: tagging mail to act on it en masse
- [other mail commands status]: marking mail unread, and setting its status

## Tagging mail

- `@mail/tag [<msg-list> | all]`
- `@mail/untag [<msg-list> | all]`
* These commands tag or untag mail in the current folder.
* Tagged mail can be later acted on en masse by using "tagged" as the msg-list for other commands (which does **not** untag them afterward).
* If no msg-list is specified, all messages in the current folder are tagged/untagged.
* If "all" is given as the msg-list, all mail in **all** folders is tagged/untagged.

### Example
```sharp
To clear all mail from Paul and Chani:
@mail/tag *paul
@mail/tag *chani
@mail/clear tagged
@mail/untag all
```

## Unread and status

- `@mail/unread [<msg-list> | all]`
* Mark messages which have already been read as new/unread.

- `@mail/status [<msg-list> | all]=<status>`
* Set the status of the given messages.
* *<status>* can be one of: tagged, untagged, cleared, uncleared, read, unread, urgent or unurgent.
* Read marks a new message as read without reading it, urgent/unurgent toggle the urgent flag, and the others are equivalent to @mail/tag, @mail/untag, @mail/clear, @mail/unclear and @mail/unread respectively.
