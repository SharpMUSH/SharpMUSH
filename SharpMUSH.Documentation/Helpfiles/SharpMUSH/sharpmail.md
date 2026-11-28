# Mail
# @mail

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
* An alias name (see [@malias])


::: seealso
- [MAIL-SENDING]
- [MAIL-REPLYING]
- [MAIL-READING]
- [MAIL-FOLDERS]
- [@mail/fwd]
- [MAIL-OTHER]
- [MAIL-ADMIN]
- [@malias]
- [MAIL-REVIEWING]
- [@MAILQUOTA]
:::

# Reading Mail
# Mail-reading
# @mail/read
# @mail/list
# @mail/cstats

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

# Sending Mail
# Mail-sending
# @mail/send
# @mail/fwd

- `@mail[/switch] <player-list>=[<subject>]/<msg>`
* This sends the message *<msg>* to all players in *<player-list>*.
* If no subject is given, the message subject is the beginning of the message itself. To include a literal / in the subject, double it up (//).
* All function substitutions are valid in *<msg>* including mail(#) which will allow you to forward mail you have received to other users.

The following switches are available:
* `/send` - same as no switch
* `/urgent` - mail is marked as "Urgent"
* `/silent` - no notification to sender that mail was sent
* `/nosig` - no mail signature
* `/noeval` - `<player-list>`, `<subject>` and `<msg>` are not evaluated
* `/quick` - TinyMUX's form, `@mail/quick <player-list>/<subject>=<msg>`: the subject sits on the left

If you have an [@mailsignature] attribute set on yourself, its contents will be evaluated and appended to the message unless the /nosig switch is given.

To write a longer message a line at a time, with prompts for its recipients and subject, use [+compose] where the game has it.

A message number in *<player-list>* replies to its sender; see [MAIL-REPLYING].

- `@mail/fwd <msg-list>=<player-list>`
* This sends a copy of all the messages in *<msg-list>* to all the players in *<player-list>*. The copy will appear to have been sent by you (not the original sender), and its status will be "Forwarded". `/forward` is the same switch.

# Replying to Mail
# Mail-replying
# @mail/reply
# @mail/replyall
# @mail/quote

- `@mail/reply[/<switches>] <msg #>=<message>`
- `@mail/replyall[/<switches>] <msg #>=<message>`
- `@mail <msg #>=[<subject>/]<message>`

`@mail/reply` sends *<message>* to the sender of *<msg #>*. `@mail/replyall` sends it to the sender and to everyone else the sender addressed, except you. If the sender used a mail alias, the reply goes to that alias, and its members get it if you may use the alias.

The reply's subject is the original's with `Re: ` in front, added only once. To give another subject, use `@mail <msg #>=<subject>/<message>`. The reply joins the original's conversation (see [@mail/thread]).

Switches:
* `/quote` - add the original below your message, each line starting with `> `
* `/urgent`, `/silent`, `/nosig`, `/noeval` - as for [@mail/send]

Both switches also take the id of a message you hold in place of *<msg #>*, as the website does. The reply then answers that message even if your mail is renumbered. You can't reply to anonymous mail, or to mail whose sender no longer exists, but `@mail/replyall` still reaches the other recipients.

### Example
```
> @mail/replyall 4=Count me in.
MAIL: You sent your message to Bob.
MAIL: You sent your message to Ann.
```

# Mail Threads
# Mail-threads
# @mail/thread

- `@mail/thread <msg #>`
- `@mail/thread <thread id>`

Every message belongs to a conversation, called a thread. A message sent to several players is one message in one thread. A reply joins the thread of the message it answers. A new message starts a thread of its own, and so does a forward.

`@mail/thread` lists the thread a message is in: every message in it that you sent or received, with each reply under the message it answers. A number such as `0:13` is the folder and number of your copy. A message without one is mail you sent and do not hold. Replies you were not sent are not shown. The last line gives the thread's id, which [mailthread()] also returns and `@mail/thread` takes in place of a message number.

### Example
Ann and Bob reply to each other on their own, then Ann replies to everyone:
```
> @mail/thread 13
=< Mail thread: Lunch >========================================
0:13 Bob to Ann, Cy * Sat Nov 28 00:45
|- Ann to Bob * Sat Nov 28 00:45
|  `- 0:14 Bob to Ann * Sat Nov 28 00:46
`- Ann to Bob, Cy * Sat Nov 28 00:46
===============================================================
4 messages. Thread 01a21f9d-5ec1-7700-b6a9-ed0b1fdef88b
```
Cy sees only `0:8 Bob to Ann, Cy` and Ann's reply to everyone under it.

# Mail Folders
# Mail-folders
# @mail/folder
# @mail/unfolder
# @mail/file

The MUSH mail system allows each player 16 folders, numbered from 0 to 15. Mail can only be in 1 folder at a time. Folder 0 is the "inbox" where new mail is received. Most @mail commands operate on only the current folder.

- `@mail/folder`
* This commands lists all folders which contain mail, telling how many messages are in each, and what the current folder is.

- `@mail/folder <folder#|foldername>`
* This command sets your current folder to *<folder#>*.

- `@mail/folder <folder#> = <foldername>`
* This command gives *<folder#>* a name.

- `@mail/unfolder <folder#|foldername>`
* This command removes a folder's name

- `@mail/file <msg-list>=<folder#|foldername>`
* This command moves all messages in *<msg-list>* from the current folder to a new folder, *<folder#>*.
* A name none of your folders has yet makes a new folder of that name, with the lowest folder number not in use.


::: seealso
- [@MAILFILTER]
:::

# Reviewing Mail
# Mail-reviewing
# @mail/review
# @mail/retract

- `@mail/review [<player>]`
* Reviews the messages you have sent to *<player>*, or all messages you've sent if no *<player>* is specified.

- `@mail/review <player>=<msglist>`
* Reads the messages you have sent to *<player>*.

- `@mail/retract <player>=<msglist>`
* Retracts (deletes) unread messages you have sent to *<player>*.

# @mailquota

- `@mailquota <player>[=<limit>]`

This attribute allows wizards to change the maximum number of messages a player can have in their inbox, override the mail_limit @config option for specific people. *<limit>* should be a number between 1 and 50000, inclusive.

### Example
```sharp
> @alias *Walker=Complaints_Department
> @mailquota *Complaints_Department=50000
> @wall Please @mail any and all problems to Complaints_Department.
```

# @mailfilter
# MAILFILTER

The @mailfilter attribute specifies automatic filing of incoming @mail messages into folders. When an @mail message is received, the contents of @mailfilter are evaluated, with the following arguments passed:
* `%0` or `%<sender>` - dbref of message sender
* `%1` or `%<subject>` - message subject
* `%2` or `%<body>` - message body
* `%3` or `%<flags>` - message status flags (a string containing U and/or F, for urgent and/or forwarded)

Each value is passed by position and by the name shown (see [named arguments]). Code written for PennMUSH reads the positions.

If @mailfilter evaluates to a folder name or number, the message will be filed into that folder. If @mailfilter evaluates to a null string, the message remains in the incoming folder.

### Example
Filter urgent messages into folder 1
```sharp
> @mailfilter me=if(strmatch(%3,*U*),1)
```

The same, reading the flags by name:
```sharp
> @mailfilter me=if(strmatch(%<flags>,*U*),1)
```


::: seealso
- [MAIL-FOLDERS]
:::

# @mailsignature
# MAILSIGNATURE

- `@mailsignature <object>[=<signature>]`

When set, this attribute is evaluated and appended to any @mail messages sent by *\<object\>*, unless the @mail/nosig command is used.

### Example
```sharp
> @mailsignature me=%r%r-- %n%r%r[u(funny_quote)]
```


::: seealso
- [MAIL]
- [MAIL-SENDING]
:::

# Mail Administration
# Mail-admin

The @mail command can also take the following switches:

- `@mail/stats [<player>]` - Basic mail statistics.
- `@mail/dstats [<player>]` - Also provides read/unread count.
- `@mail/fstats [<player>]` - Does all that, plus gives space usage.

- `@mail/debug <action>[=<player>]`
- `@mail/nuke`

Only wizards may stats players other than themselves.

The /debug switch does sanity checking on the mail database, and needs the chat administration permission. `@mail/debug sanity` just does the check; the command `@mail/debug clear=<player name or dbref number>` wipes mail for an object. `@mail/debug fix` attempts to repair problems noted in the sanity check.

The /nuke switch destroys the post office, erasing all @mail everywhere. It may only be used by God.

# Mail Functions

Mail functions work with @mail. Some come from TinyMUX (see [tinymux mail functions]) and some from RhostMUSH (see [rhostmush mail functions]).

|                   |                   |                   |                   |
|-------------------|-------------------|-------------------|-------------------|
| [FOLDERCURRENT()] | [FOLDERLIST()]    | [FOLDERSTATS()]   | [MAIL()]          |
| [MAILALIAS()]     | [MAILCOUNT()]     | [MAILDSTATS()]    | [MAILFLAGS()]     |
| [MAILFROM()]      | [MAILFSTATS()]    | [MAILINFO()]      | [MAILLIST()]      |
| [MAILQUICK()]     | [MAILQUOTA()]     | [MAILREAD()]      | [MAILREVIEW()]    |
| [MAILSEND()]      | [MAILSIZE()]      | [MAILSTATS()]     | [MAILSTATUS()]    |
| [MAILSUBJECT()]   | [MAILTHREAD()]    | [MAILTIME()]      | [MALIAS()]        |
| [MSIZETOT()]      |                   |                   |                   |

# folderstats()

- `folderstats()`
- `folderstats(<folder #>)`
- `folderstats(<player>)`
- `folderstats(<player>, <folder #>)`

folderstats() returns the number of read, unread, and cleared messages in a specific folder, or, if none is given, the player's current folder. Only Wizards may use forms which get other players' mail information.


::: seealso
- [mailstats()]
:::

# mail()

- `mail()`
- `mail(<player name>)`
- `mail([<folder #>:]<mail message #>)`
- `mail(<player>, [<folder #>:]<mail message #>)`

Without arguments, mail() returns the number of messages in all the player's mail folders. With a player name argument, mail() returns the number of read, unread, and cleared messages *<player>* has in all folders. Only Wizards can use this on other players.

When given numeric arguments, mail() returns the text of the corresponding message in the current folder. The message number may also be prefaced by the folder number and a colon, to indicate a message in a different folder.

### Example
```sharp
> think mail(3:2)
(text of the second message in the player's third folder)
```


::: seealso
- [maillist()]
- [mailfrom()]
:::

# maillist()

- `maillist([<player>, ]<message-list>)`

maillist() returns a list of all *<player>*'s @mail messages which match the given *<message-list>* (the same as @mail/list *<message-list>*). If no *<player>* is given, the executor's mail is matched. The *<message-list>* argument is described in [MAIL].

### Examples
```sharp
> think maillist()
0:1 0:2 0:3
> think maillist(all)
0:1 0:2 0:3 1:1 1:2
> think maillist(1:)
1:1 1:2
```


::: seealso
- [MAIL]
- [mailfrom()]
:::

# mailfrom()
# mailtime()
# mailstatus()
# mailsubject()

- `mailfrom([<player>, ][<folder #>:]<mail message #>)`
- `mailtime([<player>, ][<folder #>:]<mail message #>)`
- `mailstatus([<player>, ][<folder #>:]<mail message #>)`
- `mailsubject([<player>, ][<folder #>:]<mail message #>)`

* mailfrom() returns the dbref number of the sender of a mail message.
* mailtime() is similar, but returns the time the mail was sent.
* mailsubject() is similar, but returns the subject of the message.
* mailstatus() returns the mail's status characters (as per @mail/list).


::: seealso
- [MAIL]
- [maillist()]
:::

# mailthread()

- `mailthread([<player>, ][<folder #>:]<mail message #>)`

mailthread() returns the id of the thread a message is in, which [@mail/thread] takes. Every copy of a message, and every reply to it, has the same thread id.

### Example
```sharp
> think mailthread(6)
01a21f66-7e2c-79b0-b543-c4f7a4ceee69
```

::: seealso
- [@mail/thread]
:::

# mailstats()
# maildstats()
# mailfstats()

- `mailstats([<player>])`
- `maildstats([<player>])`
- `mailfstats([<player>])`

The mail*stats() functions return data like @mail/*stats does. You either must use this on yourself, or you must be a wizard. The information will be joined together as a space separated list of numbers.

### Example
```sharp
> think mailstats(One)
<# sent> <# received>
> think mailfstats(One)
<# sent> <# sent unread> <# sent cleared> <# sent bytes> <# received>
<# received unread> <# received cleared> <# received bytes>
```


::: seealso
- [folderstats()]
:::

# mailsend()

- `mailsend(<player>,[<subject>/]<message>)`
- `mailsend(<players>,<subject>,<message>)`

This function sends a message to a player, just like @mail/send. It returns nothing if successful, or an error message. The three-argument form is TinyMUX's: the subject is given apart, and it returns 1 once the mail is delivered.

# malias()

- `malias([<delimiter>])`
- `malias(<malias name>)`
- `malias(<malias name>[,<delimiter>])`

With no arguments, malias() returns the list of all malias names which are visible to the player. With two arguments, returns the list of dbrefs that are members of the given malias, delimited by *<delimiter>*.

With one argument, the behavior is ambiguous:
* If the argument matches a malias, returns the list of dbrefs that are members of the malias, space-delimited
* If not, it's treated as a no-argument case with a delimiter
