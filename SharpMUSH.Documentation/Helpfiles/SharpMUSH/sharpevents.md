# Events
# Event
SharpMUSH Events are hardcoded events that may or may not be caused by players. An object designated as the event handler (via the "event_handler" config option) has attributes triggered on it, with arguments, on specified events.

Unlike PennMUSH, **SharpMUSH pre-populates the event handler**: a new database is seeded with an **Event Handler object (#9)**, and the "event_handler" config option already points at it. You do not create one or set the config; you simply add attributes named after the events you care about:

```sharp
> &<event name> #9=<action list>
```

If you would rather use a different object, point the config at it with `@config/set event_handler=<dbref>` and set the "event_handler" option in your mush.cnf so it survives dumps and is receiving events even during startup. On a fresh instance this is optional.

**How handler code runs:**
- Event attributes run with the handler object's **own permissions** (it executes as itself, like the HTTP handler and any normal attribute). The seeded Event Handler **#9 is a WIZARD object**, so out of the box it can `@set`, `@power`, `@lock`, and see-all as an admin handler needs. If you point event_handler at your own object, flag it wizard (`@set <obj>=wizard`) to grant it those powers.
- An event is **queued**, as in PennMUSH: the attribute becomes a queue entry of its own, run by the handler and counted against the handler's queue. The command that caused the event does not wait for it, and the event gets its own "queue_entry_cpu_time" limit rather than sharing the causer's. A connecting player's auto-look, for instance, is shown before PLAYER\`CONNECT runs.
- The enactor (%#) is the executor that caused the event. For a **system event with no executor** (a scheduled check, a connection at the connect screen), %# is **#1 (God)**, not #-1. If the causer has been destroyed since, %# is #1 as well. Because %# is a real dbref either way, use an event's own arguments (not %#) to distinguish system- from player-caused triggers.


**Several attributes on one event:** the handler runs the attribute named after the event, then each attribute one level below it, in name order. A package adds its own code to an event as one of those, named after itself, and never edits the event's own attribute or another package's:

```sharp
> &PAGE`MESSAGE`TALLY #9=&PAGES me=[add(get(me/PAGES),1)]
> page Bob=Hi.
You paged Bob with 'Hi.'
> think get(#9/PAGES)
1
```

With the comm-feed package installed, a page now runs PAGE\`MESSAGE (if it has a value), then PAGE\`MESSAGE\`COMMFEED, then PAGE\`MESSAGE\`TALLY. Each gets the event's arguments by position and by name. An empty attribute runs nothing, so ``&PAGE`MESSAGE`COMMFEED #9=`` silences one package and leaves the others. Only one level is run: PAGE\`MESSAGE\`SCENE\`LOG is not, so a package keeps its helpers under its own attribute.

- On most events each attribute is queued as an entry of its own, as `@trigger` would queue it: one that fails or runs out of time does not affect the others.
- A **stoppable** event runs them in turn in one queue entry instead, as `@include` would, so one can call [stopevent()] to keep the ones after it from running. [event list] marks the stoppable events.


Programs outside the game can hear the same events through a webhook: see [@webhook].

::: seealso
- [EVENT LIST]
- [event examples]
- [stopevent()]
- [@webhook]
:::

# Event List
Event names are of the format *<type>\`<event>*. The 'type' is used simply to group similar events together for help.

An event marked **Stoppable** runs its attributes in turn and lets one stop the rest with [stopevent()] (see [events]).

Event syntax in the help is of the form:<br>
*<eventgroup>\`<eventname>* (*<first>*, *<second>*, ...)

The handler gets each argument by position, the first as %0, and by the name shown, as %`\<name\>` (see [named arguments]). `&OBJECT\`MOVE #9=...` can read the destination as %1 or as %`\<destination\>`. A name of the form *received/sent/commands* names the parts of a value made of numbers joined by `/`: %`\<received\>`, %`\<sent\>` and %`\<commands\>` are each one number, and the position holds the whole value, as in PennMUSH.

These are the events a SharpMUSH game raises. To see the help for a type, type [event <type>].

Event Types:
- **db**: db\`purge, db\`wcheck
- **log**: log\`err, log\`cmd, log\`wiz, log\`conn, log\`trace, log\`check
- **object**: object\`create, object\`destroy, object\`move, object\`rename, object\`flag
- **player**: player\`create, player\`connect, player\`disconnect, player\`channels, player\`warn
- **socket**: socket\`connect, socket\`disconnect, socket\`loginfail, socket\`createfail, socket\`gmcp
- **http**: http\`blocked, http\`command
- **room**: room\`contents
- **channel**: channel\`message, channel\`who
- **page**: page\`message
- **mail**: mail\`received
- **backup**: backup\`complete, backup\`error

The room, channel, page, mail and backup events, player\`channels, player\`warn and socket\`gmcp are SharpMUSH's own; PennMUSH has no equivalent. The room, channel and page events exist so a handler can send a web client structured updates (the bundled room-contents and comm-feed packages), and each names exactly who the update concerns, so a handler never has to work out again who could see what. player\`warn is how a warning from the web portal reaches the game, and socket\`gmcp hands softcode what a client sends over GMCP.

PennMUSH documents some events SharpMUSH never raises, because it has nothing to raise them from: the dump, sql and signal events, db\`dbck, log\`huh, player\`inactivity and http\`fail. [compatibility events] lists them with what to use instead.

An argument of a SharpMUSH event that holds text a player wrote, such as a channel line or a page, keeps its colour: `%<message>` in a page\`message handler is the page as the recipients saw it. Use stripansi() for the plain text. PennMUSH's events pass plain text, as PennMUSH does.

# Database Events
# Event DB
- **db\`purge**
- Triggered after the scheduled purge of destroyed objects, every purge_interval.
- **db\`wcheck**
- Triggered after the scheduled @warnings check of every object, every warn_interval.

**Note**: These events are only triggered after the scheduled runs, and not when someone runs `@purge` or `@wcheck`. %# is #1.

# Log Events
# Event Log
Events in the log tree are triggered when `@log` writes a message to a log. The switch names the log: `@log/err` triggers log\`err, and `@log` with no switch triggers log\`cmd. %# is whoever ran `@log`. The server's own log lines (connections, errors) go to the server log and do not trigger these.

- **log\`err** (*message*)
- **log\`cmd** (*message*)
- **log\`wiz** (*message*)
- **log\`conn** (*message*)
- **log\`trace** (*message*)
- **log\`check** (*message*)

*<message>* is the message as logged, as plain text.

### Example
```sharp
> &LOG`WIZ #9=@cemit Admin=Logged by [name(%#)]: %<message>
> @log/wiz Moved the plot room.
Message logged to Wizard log.
```

# Object Events
# Event Object
- **object\`create** (*objid*, *clone*)
- Triggered on the creation of any object except player. If it was created using `@clone`, then *<clone>* will be a objid. Otherwise *<clone>* will be null.

- **object\`destroy** (*objid*, *name*, *type*, *owner*, *parent*, *zone*)
- Triggered _after_ the object is totally destroyed. Passed arguments are former objid, name, type, owner, etc. Enactor is always #-1, so use former owner.

- **object\`move** (*objid*, *destination*, *origin*, *silent*, *cause*)
- Triggered after the object is moved, `@tel'd`, or otherwise sent to a new location. If *<silent>* is 1, then the object was moved using `@tel/silent`.

- **object\`rename** (*objid*, *name*, *oldname*)
- Triggered when any object is renamed.
    
- **object\`flag** (*objid*, *flag*, *type*, *set*, *change*)
- Triggered when a flag or power which has the "event" restriction is set or cleared. *<type>* is one of FLAG or POWER. *<set>* is 1 if the flag/power is being set, and 0 if it's being cleared. *<change>* is either "SET" or "CLEARED".

### Example
```sharp
&OBJECT`FLAG #9=@cemit Admin=capstr(lcstr(%2)) %1 [lcstr(%4)] on [name(%0)] by %n.
```

# Player Events
# Event Player
- **player\`create** (*objid*, *name*, *how*, *descriptor*, *email*)
- Triggered when a player is created. *<how>* is one of "pcreate" or "create". If the player was `@pcreated`, %# is whoever did the `@pcreate`. If the player was made with `make` at the connect screen, %# is #1, *<how>* is "create" and *<descriptor>* is the connection that made it. *<email>* is empty: an account's email belongs to the account, not to the character.

- **player\`connect** (*objid*, *connections*, *descriptor*)
- Similar to `@aconnect`, but for events, and so you can use descriptor.

- **player\`disconnect** (*objid*, *connections*, *hidden*, *cause*, *ip*, *descriptor*, *connected*, *idle*, *received/sent/commands*)
- Similar to `@adisconnect`, but for event system, and with more information available.

- **player\`channels** (*objid*, *cause*, *channel*)
- Triggered for a connected player when their channel list may have changed. *<cause>* is one of connect, resume, join, leave, status (a change to their own channel flags, such as a gag or a title), rename or delete; *<channel>* is the channel concerned (its new name after a rename), and empty on connect and resume. resume is a web connection that came back to its session, still logged in (a page reload, or a dropped connection), and is sent the list again. Not triggered for a player who is not connected: they are sent their whole list when they connect.

- **player\`warn** (*objid*, *reason*, *account*)
- Stoppable. Triggered when staff warn a character from the web portal's moderation page. %# is the staff member's character, *<objid>* the warned character, *<reason>* what staff wrote, and *<account>* the staff member's account name. SharpMUSH keeps no warning itself beyond the audit log entry: the handler decides what a warning does, such as sending mail, opening a job or keeping a count. Nothing happens when the handler has no PLAYER\`WARN attribute.

# Socket Events
# Event Socket
- **socket\`connect** (*descriptor*, *ip*)
- Triggered when a socket first connects to the port. Using both this and player\`connect could be spammy. This happens when a connecting socket sees the connect screen.

- **socket\`disconnect** (*descriptor*, *ip*, *cause*, *received/sent/commands*)
- Triggered when a socket disconnects. Using this and player\`disconnect could be spammy.

- **socket\`loginfail** (*descriptor*, *ip*, *count*, *reason*, *objid*, *name*)
- Triggered when a login attempt fails. *<count>* is the number of fails in the past 10 minutes. If used in conjuction with the config option connect_fail_limit, then any failures after the limit is reached will **NOT** trigger socket\`loginfail. If the connect is a failed attempt to log into a valid player, *<objid>* will be set to that objid. Otherwise it will be set to #-1. *<name>* is the name the connection attempted to connect with, and is only set when *<objid>* is #-1.

- **socket\`createfail** (*descriptor*, *ip*, *count*, *reason*, *name*, *error*)
- Triggered when the connect screen refuses to create an account (`register`) or a character (`make`). *<reason>* is "creation disabled" when player_creation is off, "name taken" or "bad name" for a character name, or the account service's own message for a refused account. *<name>* is the name asked for, empty when creation is disabled. *<count>* is always 1, and *<error>* always empty: SharpMUSH sends no registration mail.

**Note**: A sitelock rule with deny_silent will not trigger socket\`loginfail or socket\`createfail.

- **socket\`gmcp** (*descriptor*, *package*, *message*, *player*)
- Stoppable. Triggered when a telnet client sends a GMCP package, such as Mudlet's External.Discord.Hello when it connects. *<message>* is the package's JSON, empty when it sent none. *<player>* is whoever is logged in on the descriptor, empty at the connect screen; %# is that player too, or #1 before login. Answer with oob() on the descriptor, which reaches the client before anyone logs in. Core.Ping and Core.KeepAlive are answered by the server and do not trigger this. The client chooses everything it sends, so check *<package>* before acting on *<message>*.

# HTTP Events
# Event HTTP
- **http\`blocked** (*descriptor*, *ip*, *method*, *path*, *reason*)
- Triggered when an HTTP request is sitelocked !connect, by IP or path. *<reason>* is "IP sitelocked !connect" or "path sitelocked !connect". *<descriptor>* is empty: an HTTP request has no connection of its own.

- **http\`command** (*ip*, *method*, *path*, *status*, *content-type*, *request-length*, *response-length*)
- Triggered after an HTTP command is executed, using the same remaining request lifetime. It is omitted if that lifetime has already expired. If the event exhausts the deadline, the HTTP response is 503; request cancellation also cancels the event.

**Note**: A sitelock rule with deny_silent will not trigger http\`blocked

# Room Events
# Event Room
- **room\`contents** (*room*, *cause*)
- Triggered, for the room rather than for whoever moved, whenever what is in a room changes: something enters or leaves it, or a player connects or disconnects in it. *<cause>* is one of move-in, move-out, connect, disconnect or resume. %# is whoever caused it: the mover, or the wizard who `@tel`'d them. resume is a web connection that came back to its session, still logged in (a page reload, or a dropped connection): nothing in the room changed, %# is that player, and only they need the room again.

# Channel Events
# Event Channel
- **channel\`message** (*channel*, *speaker*, *style*, *name*, *message*, *recipients*, *time*, *id*)
- Triggered after a channel line has been delivered. *<recipients>* is the objids of exactly the members it was delivered to: a member who has gagged the channel, one the speaker may not be heard by, one whose `@chatformat` silenced the line, and a muted member's copy of a connect or disconnect line are not among them. Nobody received it, nothing is triggered. *<style>* is one of say, pose, semipose, emit or presence (a connect or disconnect line). *<name>* and *<message>* keep their colour, as the channel's mogrifier left them. *<speaker>* is empty for a line with no speaker, and both it and *<name>* are empty for an `@cemit` line, which does not name its emitter (%# is still the emitter). *<time>* is milliseconds since 1970. *<id>* is the line's id, the one `@channel/recall`'s buffer holds it under (and the web portal reads it back by); ids rise with time, so a later line has a larger one.

- **channel\`who** (*channel*, *member*, *name*, *change*, *viewers*, *cause*)
- Triggered when a member comes onto or goes off a channel's member list, as `@channel/who` lists it: a thing always, a player while connected, and a member hiding on the channel only to a viewer who may see hidden members. *<viewers>* is the objids of the connected player members whose view of the list changed, and *on* or *off* is what changed for them; a change seen differently by different members (someone starting `@channel/hide`) triggers once for each. *<cause>* is one of connect, disconnect (the last connection closing), join, leave or status (hiding or no longer hiding). %# is the member.

# Page Events
# Event Page
- **page\`message** (*speaker*, *recipients*, *style*, *name*, *message*, *time*, *id*)
- Triggered after a page reaches at least one recipient. *<recipients>* is the objids of the players it reached: one who is not connected, is HAVEN, refuses pages from the pager, or whose page lock the pager fails is not among them. *<style>* is one of say, pose or semipose. *<name>* is as the recipients' terminal shows it, with the page alias when page_aliases is on. *<message>* keeps its colour. *<time>* is milliseconds since 1970. *<id>* is the page's id, from the sequence channel line ids come from; when the page_log option is on, it is the id the page log keeps the page under (see [page log]).

# Mail Events
# Event Mail
- **mail\`received** (*recipient*, *sender*, *number*, *subject*, *urgent*)
- Triggered after @mail is delivered to a player's mailbox, once for each recipient, before their MAILFILTER files it. *<number>* is the message's number in the recipient's inbox, *<subject>* keeps its colour, and *<urgent>* is 1 for `@mail/urgent`. %# is the sender. The body is not passed: read it with mail() as the recipient if you need it.

### Example
```sharp
> &MAIL`RECEIVED #9=@cemit Admin=[name(%<sender>)] mailed [name(%<recipient>)]: %<subject>
```

# Backup Events
# Event Backup
- **backup\`complete** (*name*, *bytes*)
- Triggered after `@backup` or a scheduled backup (SHARPMUSH_BACKUP_INTERVAL) has written a copy of the world. *<name>* is the copy's name, as `@backup/list` shows it, and *<bytes>* its size.

- **backup\`error** (*reason*)
- Triggered when a copy could not be written. *<reason>* is why, as `@backup` reports it.

%# is whoever ran `@backup`, or #1 for a scheduled copy.

### Example
```sharp
> &BACKUP`ERROR #9=@cemit Admin=Backup failed: %<reason>
```
