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


::: seealso
- [EVENT LIST]
- [event examples]
- [stopevent()]
:::

# Event List
Event names are of the format *<type>\`<event>*. The 'type' is used simply to group similar events together for help.

An event marked **Stoppable** runs its attributes in turn and lets one stop the rest with [stopevent()] (see [events]).

Event syntax in the help is of the form:<br>
*<eventgroup>\`<eventname>* (*<first>*, *<second>*, ...)

The handler gets each argument by position, the first as %0, and by the name shown, as %`\<name\>` (see [named arguments]). `&OBJECT\`MOVE #9=...` can read the destination as %1 or as %`\<destination\>`. A name of the form *received/sent/commands* names the parts of a value made of numbers joined by `/`: %`\<received\>`, %`\<sent\>` and %`\<commands\>` are each one number, and the position holds the whole value, as in PennMUSH.

These are the events a SharpMUSH game raises. To see the help for a type, type [event <type>].

Event Types:
- **dump**: dump\`5min, dump\`1min, dump\`complete, dump\`error
- **db**: db\`dbck, db\`purge, db\`wcheck
- **log**: log\`err, log\`cmd, log\`wiz, log\`conn, log\`trace, log\`check, log\`huh
- **object**: object\`create, object\`destroy, object\`move, object\`rename, object\`flag
- **attribute**: attribute\`change
- **sql**: sql\`connect, sql\`connectfail, sql\`disconnect
- **signal**: signal\`usr1, signal\`usr2
- **player**: player\`create, player\`connect, player\`disconnect, player\`inactivity, player\`channels, player\`warn, player\`link, player\`unlink
- **socket**: socket\`connect, socket\`disconnect, socket\`loginfail, socket\`createfail, socket\`gmcp, socket\`timezone
- **http**: http\`blocked, http\`fail, http\`command
- **room**: room\`contents
- **channel**: channel\`message, channel\`who
- **page**: page\`message
- **mail**: mail\`received
- **scene**: scene\`status
- **backup**: backup\`complete, backup\`error
- **gallery**: gallery\`add, gallery\`report

The attribute, room, channel, page, mail, scene, backup and gallery events, player\`channels, player\`warn, player\`link, player\`unlink, socket\`gmcp and socket\`timezone are SharpMUSH's own; PennMUSH has no equivalent. The room, channel and page events exist so a handler can send a web client structured updates (the bundled room-contents and comm-feed packages), and each names exactly who the update concerns, so a handler never has to work out again who could see what. attribute\`change tells the game an attribute flagged `watched` changed, so a gauge or a character sheet need not check it over and over, player\`warn is how a warning from the web portal reaches the game, socket\`gmcp hands softcode what a client sends over GMCP, socket\`timezone the time zone a client reports, the gallery events tell the game about the web portal's profile pictures, and scene\`status lets a package act when a scene starts or ends.

An argument of a SharpMUSH event that holds text a player wrote, such as a channel line or a page, keeps its colour: `%<message>` in a page\`message handler is the page as the recipients saw it. Use stripansi() for the plain text. PennMUSH's events pass plain text, as PennMUSH does.

# Dump Events
# Event Dump
A SharpMUSH game writes every change to disk as it happens, so its dump is a backup: `@backup` and `@dump` take one at once, and the server takes one every SHARPMUSH_BACKUP_INTERVAL when that is set, warning everyone five minutes and one minute before. Each warning is given only when the interval is longer than it.

- **dump\`5min** (*message*, *forking*)
- A scheduled backup will be taken in 5 minutes.
- **dump\`1min** (*message*, *forking*)
- A scheduled backup will be taken in 1 minute.
- **dump\`complete** (*message*, *forking*)
- A backup was written.
- **dump\`error** (*message*, *forking*, *status*)
- A backup could not be written. You might want this to alert any admin on.

*<message>* is the message shown to everyone: dump_warning_5min, dump_warning_1min, dump_complete, or for dump\`error "GAME: ERROR! Database save failed!", which goes to connected wizards and royalty. *<forking>* is always 0: a backup never forks. *<status>* is "PERROR " followed by the reason the backup failed.

The standard messages are still shown when these events are set. To turn one off, set it to an empty string with `@config/set` or in mush.cnf. %# is whoever ran `@backup` or `@dump`, or #1 for a scheduled backup. backup\`complete and backup\`error are raised alongside, with the copy's name and size.

### Example
```sharp
> &DUMP`ERROR #9=@cemit Admin=Save failed: %<status>
```

# Database Events
# Event DB
- **db\`dbck**
- Triggered when `@dbck` has read the newest backup through without a fault. SharpMUSH keeps its links consistent as it writes them, so its check reads the copy you would restore instead of the live world. %# is whoever ran `@dbck`.
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

- **log\`huh** (*message*)
- Triggered when a player types a command the game does not understand (Huh?) somewhere they do not control. *<message>* is PennMUSH's huh log line: "HUH: Name(#dbref) in Room(#dbref) [Room's owner]: command". %# is the player.

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

# Attribute Events
# Event Attribute
- **attribute\`change** (*objid*, *attribute*, *old*, *new*, *enactor*)
- Triggered after an attribute flagged `watched` is changed: set with `&` or `@set`, by set() or another function, copied or moved onto with `@cpattr` or `@mvattr`, cleared, or wiped. *<objid>* is the object the attribute is on, *<attribute>* its full name in upper case (HP, or STATS\`HP), *<old>* the value it had and *<new>* the value it has now, both keeping their colour. A new attribute's *<old>* is empty, and a cleared or wiped attribute's *<new>* is empty; a `@wipe` triggers once for each watched attribute it removes. *<enactor>* is whoever changed it, which %# is too, or #1 when the server changed it for its own bookkeeping. Setting an attribute to the value it already has still triggers it.

Only attributes flagged `watched` trigger it (see [attribute flags]), so attributes nobody watches cost nothing. Mark one with `@set <object>/<attribute>=watched`, or a standard attribute wherever it is created from then on with `@attribute/access <attribute>=watched`. The flag watches the attribute it is set on, not the ones below it. Loading a PennMUSH database triggers nothing.

The handler runs after the change has happened, so the event cannot be stopped, and it is private: the values may be hidden from everyone but the owner.

**Changes made by the handler:** a handler that changes a watched attribute (the one it heard about, or another) triggers attribute\`change again, and so does anything the handler queues with `@trigger`, `@wait` or `@force`. To keep a handler that changes what it heard about, or two handlers that change each other's attributes, from running for ever, a chain stops after 10 events: a change made by the handler of the tenth is still made, but triggers nothing, and the server logs a warning. A change typed by a player or made by any other code starts a new chain.

### Example
```sharp
> &HP me=20
> @set me/HP=watched
> &ATTRIBUTE`CHANGE`LOG #9=@assert strmatch(%<attribute>,HP) ; @pemit %<objid>=HP went from %<old> to %<new>.
> &HP me=15
```
Output: after the last line you are told "HP went from 20 to 15."

See [event examples gauge example] for a health gauge sent to the player's client.

# SQL Events
# Event SQL
SharpMUSH opens a connection for each sql() query, so these report a change: whether the latest attempt to reach the SQL server worked, against the one before it.

- **sql\`connect** (*platform*)
- Triggered on successful connect to the SQL database, the first time and after a failure. *<platform>* is 'mysql', 'postgresql' or 'sqlite3'.

- **sql\`connectfail** (*platform*, *error*)
- Triggered on unsuccessful connect to the SQL database, when the attempt before it failed too or there was none. *<error>* is the database driver's message.

- **sql\`disconnect** (*platform*, *error*)
- Triggered when the SQL database cannot be reached after it could be. sql() tries again on its next query.

%# is whoever ran the query.

# Signal Events
# Event Signal
No arguments are passed to these events.

- **signal\`usr1**: Triggered when the SharpMUSH server process receives a "kill -USR1"
- **signal\`usr2**: Triggered when the SharpMUSH server process receives a "kill -USR2"

If these attributes exist, then SharpMUSH will **NOT** perform what it usually does when it receives a signal. In effect, these override the default actions: USR1 restarts the server, as `@shutdown/reboot` does, and USR2 takes a backup, as `@dump` does. Players stay connected through a restart.

To mimic old behaviour:
```sharp
&SIGNAL`USR1 #9=@nspemit/list lwho()=GAME: Reboot w/o disconnect from game account, please wait. ; @shutdown/reboot
&SIGNAL`USR2 #9=@dump
```

# Player Events
# Event Player
- **player\`create** (*objid*, *name*, *how*, *descriptor*, *email*)
- Triggered when a player is created. *<how>* is one of "pcreate" or "create". If the player was `@pcreated`, %# is whoever did the `@pcreate`. If the player was made with `make` at the connect screen, %# is #1, *<how>* is "create" and *<descriptor>* is the connection that made it. *<email>* is empty: an account's email belongs to the account, not to the character.

- **player\`connect** (*objid*, *connections*, *descriptor*)
- Similar to `@aconnect`, but for events, and so you can use descriptor.

- **player\`disconnect** (*objid*, *connections*, *hidden*, *cause*, *ip*, *descriptor*, *connected*, *idle*, *received/sent/commands*)
- Similar to `@adisconnect`, but for event system, and with more information available.

- **player\`inactivity**
- Triggered when idle players are disconnected. Only run if at least one player gets idlebooted (Or auto-hidden), not at every inactivity check. The check runs every minute: a player idle longer than idle_timeout is disconnected, unless they may idle (the Idle power, wizards and royalty); one who may idle and is UNFINDABLE is hidden instead, if they may hide. A web portal tab is never idlebooted. With idle_timeout 0, the default, nobody is.

- **player\`channels** (*objid*, *cause*, *channel*)
- Triggered for a connected player when their channel list may have changed. *<cause>* is one of connect, resume, join, leave, status (a change to their own channel flags, such as a gag or a title), rename or delete; *<channel>* is the channel concerned (its new name after a rename), and empty on connect and resume. resume is a web connection that came back to its session, still logged in (a page reload, or a dropped connection), and is sent the list again. Not triggered for a player who is not connected: they are sent their whole list when they connect.

- **player\`warn** (*objid*, *reason*, *account*)
- Stoppable. Triggered when staff warn a character from the web portal's moderation page. %# is the staff member's character, *<objid>* the warned character, *<reason>* what staff wrote, and *<account>* the staff member's account name. SharpMUSH keeps no warning itself beyond the audit log entry: the handler decides what a warning does, such as sending mail, opening a job or keeping a count. Nothing happens when the handler has no PLAYER\`WARN attribute.

- **player\`link** (*objid*, *account*)
- Triggered when a character is linked to an account, however it happened: @account/link, @account/claim or claim at the connect screen, the web portal, or a package. *<account>* is the account's id, as account() returns it. A character moved from one account to another triggers player\`unlink for the old account, then player\`link for the new one. %# is #1.

- **player\`unlink** (*objid*, *account*)
- Triggered when a character is taken off an account, by @account/unlink, the web portal, or a move to another account. *<account>* is the id of the account it left. The character itself is kept. %# is #1.

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

- **socket\`timezone** (*descriptor*, *zone*, *player*)
- Triggered when a client reports its time zone, such as Europe/Paris: the web portal reports the browser's, and a telnet client its TZ through NEW-ENVIRON. *<zone>* is always a zone the server knows; anything else a client sends is dropped. It is raised once per zone a connection reports, usually before login, when *<player>* is empty and %# is #1; afterwards *<player>* and %# are whoever is logged in on the descriptor. clientzone() reads the zone at any time, so a handler that acts at login reads it from player\`connect's *<descriptor>* instead. The zone is only what the client said, and says roughly where someone is.

# HTTP Events
# Event HTTP
- **http\`blocked** (*descriptor*, *ip*, *method*, *path*, *reason*)
- Triggered when an HTTP request is sitelocked !connect, by IP or path. *<reason>* is "IP sitelocked !connect" or "path sitelocked !connect". *<descriptor>* is empty: an HTTP request has no connection of its own.

- **http\`fail** (*descriptor*, *ip*, *reason*)
- Triggered when an HTTP request reaches the http_handler and its body cannot be read: one larger than the server accepts, or malformed chunked data. *<reason>* is the web server's message, and the request is answered with its status. *<descriptor>* is empty.

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

# Scene Events
# Event Scene
- **scene\`status** (*scene*, *status*, *previous*)
- Triggered by the Scene plugin when a scene is made or its status changes: `@scene/create` and scenecreate() make one, and `@scene/set <id>/status=` or sceneset() change it, which is how `+scene/start`, `/pause` and `/finish` do it. *<scene>* is the scene's id, *<status>* its status now (new, active, paused, finished or cancelled as the scene package uses them), and *<previous>* its status before, empty for a scene just made. Setting the status it already has triggers nothing. %# is whoever made the scene or changed it. Only raised while the Scene plugin is loaded.

### Example
```sharp
&SCENE`STATUS #9=@if strmatch(%<status>,active)={@cemit Public=Scene %<scene> has started: [scene(%<scene>,title)]}
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

# Gallery Events
# Event Gallery
- **gallery\`add** (*objid*, *asset*, *url*, *use*)
- Triggered after an image is added to a character's profile gallery on the web portal. %# is the character who uploaded it, *<objid>* the character whose gallery it is, *<asset>* the stored file's id, *<url>* its site-relative address, and *<use>* one of gallery, avatar or banner: what it was uploaded as.

- **gallery\`report** (*objid*, *asset*, *url*, *reason*, *caption*)
- Triggered when someone signed in to the web portal reports a gallery image. %# is the reporter's character, *<objid>* the character whose gallery holds the image, *<reason>* what the reporter wrote, and *<caption>* the image's caption, empty when it has none. SharpMUSH keeps no report of its own: the handler decides what one does. The portal offers Report only while the event handler has GALLERY\`REPORT or a package's attribute under it; the bundled jobs package sets GALLERY\`REPORT\`JOBS, which files a job. *<reason>* is the reporter's own text, so use it as text and never evaluate it.

### Example
```sharp
&GALLERY`ADD #9=@wizwall/emit New picture for [name(%<objid>)]: %<url> as %<use>
```

See [gallery].
