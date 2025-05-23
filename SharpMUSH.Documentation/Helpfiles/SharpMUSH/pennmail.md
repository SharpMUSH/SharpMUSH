# MAIL
# @MAIL

- `@mail[/<switches>] [<msg-list>[=<target>]]`
- `@mail[/<switches>] <player-list>=[<subject>/]<message>`

@mail invokes the built-in MUSH mailer, which allows players to send and receive mail. Pronoun/function substitution is performed on any messages you may try to send.

A *<msg-list>* is one of the following:
* A single msg # (ex: 3)
* A message range (ex: 2-5, -7, 3-)
* A folder number and message number/range (ex: 0:3, 1:2-5, 2:-7)
* A sender (ex: *paul)
* An age of mail in days (ex: ~3 (exactly 3), <2, >1)
* "days" here means 24-hour periods from the current time.
* One of the following: "read", "unread", "cleared", "tagged", "urgent", "folder" (all messages in the current folder), "all" (all messages in all folders).

Unless a folder is explicitly specified, or all is used, only messages in the current folder will be counted.

A *<player-list>* is a space-separated list of recipients, which may be:
* Player names
* Player dbref #'s
* Message #'s, in which case you send to the sender of that message
* An alias name (see [help @malias|@malias])

See also:
- [help mail-sending|mail-sending]
- [help mail-reading|mail-reading]
- [help mail-folders|mail-folders]
- [help mail-forward|mail-forward]
- [help mail-other|mail-other]
- [help mail-admin|mail-admin]
- [help @malias|@malias]
- [help mail-reviewing|mail-reviewing]
- [help @mailquota|@mailquota]

# MAIL-READING
# @MAIL/READ
# @MAIL/LIST
# @MAIL/CSTATS

- `@mail <msg #>`
- `@mail/read <msg-list>`
* This displays messages which match the msg# or msg-list from your current folder.

- `@mail`
- `@mail <msg-list, but not a single msg #>`
- `@mail/list <msg-list>`
* This gives a brief list of all mail in the current folder, with sender name, time sent, and message status.
* The status field is a set of characters (ex: NC-UF+) which mean:
* N = New (unread) message
* C = Cleared message
* U = Urgent message
* F = Forwarded message
* + = Tagged message
* The opposites of these (read messages, etc.) are indicated with a '-' in the status field in that position.

`@mail/cstats`
* Shows how many messages you have, in the same format as the automatic mail check when you connect.

# MAIL-SENDING
# @MAIL/SEND
# @MAIL/FWD

- `@mail[/switch] <player-list>=[<subject>]/<msg>`
* This sends the message *<msg>* to all players in *<player-list>*.
* If no subject is given, the message subject is the beginning of the message itself. To include a literal / in the subject, double it up (//).
* All function substitutions are valid in *<msg>* including mail(#) which will allow you to forward mail you have received to other users.

The following switches are available:
* `/send` - same as no switch
* `/urgent` - mail is marked as "Urgent"
* `/silent` - no notification to sender that mail was sent
* `/nosig` - no mail signature

If you have an @mailsignature attribute set on yourself, its contents will be evaluated and appended to the message unless the /nosig switch is given.

- `@mail/fwd <msg-list>=<player-list>`
* This sends a copy of all the messages in *<msg-list>* to all the players in *<player-list>*. The copy will appear to have been sent by you (not the original sender), and its status will be "Forwarded".

# MAIL-OTHER
# @MAIL/CLEAR
# @MAIL/UNCLEAR
# @MAIL/PURGE
# @MAIL/TAG
# @MAIL/UNTAG
# @MAIL/UNREAD
# @MAIL/STATUS

- `@mail/clear [<msg-list> | all]`
- `@mail/unclear [<msg-list> | all]`
* These commands mark mail in the current folder as cleared or uncleared. Mail marked for clearing is deleted when you disconnect, or if you use @mail/purge.
* If no msg-list is specified, all mail in your current folder is cleared.
* If "all" is given instead of a msg-list, all mail in **all** folders is cleared/uncleared.

- `@mail/purge`
* Actually deletes all messages marked for clearing with @mail/clear. This is done automatically when you log out.

- `@mail/tag [<msg-list> | all]`
- `@mail/untag [<msg-list> | all]`
* These commands tag or untag mail in the current folder.
* Tagged mail can be later acted on en masse by using "tagged" as the msg-list for other commands (which does **not** untag them afterward).
* If no msg-list is specified, all messages in the current folder are tagged/untagged.
* If "all" is given as the msg-list, all mail in **all** folders is tagged/untagged.

Example:
```
To clear all mail from Paul and Chani:
@mail/tag *paul
@mail/tag *chani
@mail/clear tagged
@mail/untag all
```

- `@mail/unread [<msg-list> | all]`
* Mark messages which have already been read as new/unread.

- `@mail/status [<msg-list> | all]=<status>`
* Set the status of the given messages.
* *<status>* can be one of: tagged, untagged, cleared, uncleared, read, unread, urgent or unurgent.
* Read marks a new message as read without reading it, urgent/unurgent toggle the urgent flag, and the others are equivalent to @mail/tag, @mail/untag, @mail/clear, @mail/unclear and @mail/unread respectively.

# MAIL-FOLDERS
# @MAIL/FOLDER
# @MAIL/UNFOLDER
# @MAIL/FILE

The MUSH mail system allows each player 16 folders, numbered from 0 to 15. Mail can only be in 1 folder at a time. Folder 0 is the "inbox" where new mail is received. Most @mail commands operate on only the current folder.

- `@mail/folder`
* This commands lists all folders which contain mail, telling how many messages are in each, and what the current folder is.

- `@mail/folder <folder#|foldername>`
* This command sets your current folder to *<folder#>*.

- `@mail/folder <folder#> = <foldername>`
* This command gives *<folder#>* a name.

- `@mail/unfolder <folder#|foldername>`
* This command removes a folder's name

- `@mail/file <msg-list>=<folder#>`
* This command moves all messages in *<msg-list>* from the current folder to a new folder, *<folder#>*.

See also: [help @mailfilter|@mailfilter]

# MAIL-REVIEWING
# @MAIL/REVIEW
# @MAIL/RETRACT

- `@mail/review [<player>]`
* Reviews the messages you have sent to *<player>*, or all messages you've sent if no *<player>* is specified.

- `@mail/review <player>=<msglist>`
* Reads the messages you have sent to *<player>*.

- `@mail/retract <player>=<msglist>`
* Retracts (deletes) unread messages you have sent to *<player>*.

# @MAILQUOTA

- `@mailquota <player>[=<limit>]`

This attribute allows wizards to change the maximum number of messages a player can have in their inbox, override the mail_limit @config option for specific people. *<limit>* should be a number between 1 and 50000, inclusive.

Example:
```
> @alias *Walker=Complaints_Department
> @mailquota *Complaints_Department=50000
> @wall Please @mail any and all problems to Complaints_Department.
```

# @MAILFILTER
# MAILFILTER

The @mailfilter attribute specifies automatic filing of incoming @mail messages into folders. When an @mail message is received, the contents of @mailfilter are evaluated, with the following arguments passed:
* `%0` - dbref of message sender
* `%1` - message subject
* `%2` - message body
* `%3` - message status flags (a string containing U, F, and/or R, for urgent, forwarded, and/or reply, respectively)

If @mailfilter evaluates to a folder name or number, the message will be filed into that folder. If @mailfilter evaluates to a null string, the message remains in the incoming folder.

Example: Filter urgent messages into folder 1
```
> @mailfilter me=if(strmatch(%3,*U*),1)
```

See also: [help mail-folders|mail-folders]

# @MAILSIGNATURE
# MAILSIGNATURE

- `@mailsignature <object>[=<signature>]`

When set, this attribute is evaluated and appended to any @mail messages sent by *<object>*, unless the @mail/nosig command is used.

Example:
```
> @mailsignature me=%r%r-- %n%r%r[u(funny_quote)]
```

See also: [help @mail|@mail], [help mail-sending|mail-sending]

# MAIL-ADMIN

The @mail command can also take the following switches:

- `@mail/stats [<player>]` - Basic mail statistics.
- `@mail/dstats [<player>]` - Also provides read/unread count.
- `@mail/fstats [<player>]` - Does all that, plus gives space usage.

- `@mail/debug <action>[=<player>]`
- `@mail/nuke`

Only wizards may stats players other than themselves.

The /debug switch does sanity checking on the mail database, and may only be used by a wizard:
* `@mail/debug sanity` - just does the check
* `@mail/debug clear=<player name or dbref number>` - wipes mail for an object
* `@mail/debug fix` - attempts to repair problems noted in the sanity check

The /nuke switch destroys the post office, erasing all @mail everywhere. It may only be used by God.

# @MALIAS

- `@malias [<alias>]`

The @malias command is used to create, view, and manipulate @mail aliases, or lists. An alias is a shorthand way of specifying a list of players for @mail. Aliases begin with the '+' (plus) prefix, and represent a list of dbrefs; aliases may not include other aliases.

`@malias` with no arguments lists aliases available for your use, and is equivalent to `@malias/list`

`@malias` with a single argument (the name of an alias) lists the members of that alias, if you're allowed to see them. Other forms of the same command are `@malias/members <alias>` or `@malias/who <alias>`

See also: [help @malias2|@malias2]

# @MALIAS2

- `@malias[/create] <alias>=<player list>`
- `@malias/desc <alias>=<description>`
- `@malias/rename <alias>=<newalias>`
- `@malias/destroy <alias>`

The first form above creates a new alias for the given list of players.

`@malias/desc` sets the alias's description, which is shown when aliases are listed.

`@malias/rename` renames an alias.

`@malias/destroy` destroys the alias completely.

See also: [help @malias3|@malias3]

# @MALIAS3

- `@malias/set <alias>=<player list>`
- `@malias/add <alias>=<player list>`
- `@malias/remove <alias>=<player list>`

`@malias/set` resets the list of players on the alias to *<player list>*.

`@malias/add` adds players to the alias. Note that the same player may be on an alias multiple times.

`@malias/remove` removes players from the alias. If a player is on the alias more than once, a single remove will remove only one instance of that player.

See also: [help @malias4|@malias4]

# @MALIAS4

- `@malias/use <alias>=<perm list>`
- `@malias/see <alias>=<perm list>`

`@malias/use` controls who may use an alias. Players who may use an alias will see it in their @malias list, and can @mail to the alias.

`@malias/see` controls who may list the members of an alias.

An empty permission list allows any player. The permission list may also be a space-separated list of one or more of "owner", "members" (of the alias), and "admin".

By default, the owner and alias members may see and use the alias, but only the owner may list the members. Note that admin may always list aliases and their members, regardless of these settings, but are treated like anyone else when trying to @mail with an alias.

See also: [help @malias5|@malias5]

# @MALIAS5

- `@malias/all`
- `@malias/stat`
- `@malias/chown <alias>=<player>`
- `@malias/nuke`

`@malias/all` is an admin-only command that lists all aliases in the MUSH.

`@malias/stat` is an admin-only command that displays statistics about the number of aliases and members of aliases in use.

`@malias/chown` is a wizard-only command that changes the owner of an alias.

`@malias/nuke` is a God-only command that destroys all aliases.

# Mail Functions

Mail functions work with @mail.

Available functions:
* [help folderstats|folderstats]
* [help mail|mail]
* [help maildstats|maildstats]
* [help mailfrom|mailfrom]
* [help mailfstats|mailfstats]
* [help maillist|maillist]
* [help mailsend|mailsend]
* [help mailstats|mailstats]
* [help mailstatus|mailstatus]
* [help mailsubject|mailsubject]
* [help mailtime|mailtime]
* [help malias|malias]

# FOLDERSTATS()

- `folderstats()`
- `folderstats(<folder #>)`
- `folderstats(<player>)`
- `folderstats(<player>, <folder #>)`

folderstats() returns the number of read, unread, and cleared messages in a specific folder, or, if none is given, the player's current folder. Only Wizards may use forms which get other players' mail information.

See also: [help mailstats|mailstats]

# MAIL()

- `mail()`
- `mail(<player name>)`
- `mail([<folder #>:]<mail message #>)`
- `mail(<player>, [<folder #>:]<mail message #>)`

Without arguments, mail() returns the number of messages in all the player's mail folders. With a player name argument, mail() returns the number of read, unread, and cleared messages *<player>* has in all folders. Only Wizards can use this on other players.

When given numeric arguments, mail() returns the text of the corresponding message in the current folder. The message number may also be prefaced by the folder number and a colon, to indicate a message in a different folder.

Example:
```
> think mail(3:2)
(text of the second message in the player's third folder)
```

See also: [help maillist|maillist], [help mailfrom|mailfrom]

# MAILLIST()

- `maillist([<player>, ]<message-list>)`

maillist() returns a list of all *<player>*'s @mail messages which match the given *<message-list>* (the same as @mail/list *<message-list>*). If no *<player>* is given, the executor's mail is matched. The *<message-list>* argument is described in [help mail|mail].

Examples:
```
> think maillist()
0:1 0:2 0:3
> think maillist(all)
0:1 0:2 0:3 1:1 1:2
> think maillist(1:)
1:1 1:2
```

See also: [help mail|mail], [help mailfrom|mailfrom]

# MAILFROM()
# MAILTIME()
# MAILSTATUS()
# MAILSUBJECT()

- `mailfrom([<player>, ][<folder #>:]<mail message #>)`
- `mailtime([<player>, ][<folder #>:]<mail message #>)`
- `mailstatus([<player>, ][<folder #>:]<mail message #>)`
- `mailsubject([<player>, ][<folder #>:]<mail message #>)`

* mailfrom() returns the dbref number of the sender of a mail message.
* mailtime() is similar, but returns the time the mail was sent.
* mailsubject() is similar, but returns the subject of the message.
* mailstatus() returns the mail's status characters (as per @mail/list).

See also: [help mail|mail], [help maillist|maillist]

# MAILSTATS()
# MAILDSTATS()
# MAILFSTATS()

- `mailstats([<player>])`
- `maildstats([<player>])`
- `mailfstats([<player>])`

The mail*stats() functions return data like @mail/*stats does. You either must use this on yourself, or you must be a wizard. The information will be joined together as a space separated list of numbers.

Example:
```
> think mailstats(One)
<# sent> <# received>
> think mailfstats(One)
<# sent> <# sent unread> <# sent cleared> <# sent bytes> <# received>
<# received unread> <# received cleared> <# received bytes>
```

See also: [help folderstats|folderstats]

# MAILSEND()

- `mailsend(<player>,[<subject>/]<message>)`

This function sends a message to a player, just like @mail/send. It returns nothing if successful, or an error message.

# MALIAS()

- `malias([<delimiter>])`
- `malias(<malias name>)`
- `malias(<malias name>[,<delimiter>])`

With no arguments, malias() returns the list of all malias names which are visible to the player. With two arguments, returns the list of dbrefs that are members of the given malias, delimited by *<delimiter>*.

With one argument, the behavior is ambiguous:
* If the argument matches a malias, returns the list of dbrefs that are members of the malias, space-delimited
* If not, it's treated as a no-argument case with a delimiter