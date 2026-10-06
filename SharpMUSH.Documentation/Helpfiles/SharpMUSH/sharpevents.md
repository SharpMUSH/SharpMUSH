# EVENTS
# EVENT
SharpMUSH Events are hardcoded events that may or may not be caused by players. An object designated as the event handler (via the "event_handler" config option) has attributes triggered on it, with arguments, on specified events.

Unlike PennMUSH, **SharpMUSH pre-populates the event handler**: a new database is seeded with an **Event Handler object (#9)**, and the "event_handler" config option already points at it. You do not create one or set the config — you simply add attributes named after the events you care about:

```sharp
> &<event name> #9=<action list>
```

If you would rather use a different object, point the config at it with `@config/set event_handler=<dbref>` and set the "event_handler" option in your mush.cnf so it survives dumps and is receiving events even during startup. On a fresh instance this is optional.

**How handler code runs:**
- Event attributes run with the handler object's **own permissions** (it executes as itself, like the HTTP handler and any normal attribute). The seeded Event Handler **#9 is a WIZARD object**, so out of the box it can `@set`, `@power`, `@lock`, and see-all as an admin handler needs. If you point event_handler at your own object, flag it wizard (`@set <obj>=wizard`) to grant it those powers.
- An event is **queued**, as in PennMUSH: the attribute becomes a queue entry of its own, run by the handler and counted against the handler's queue. The command that caused the event does not wait for it, and the event gets its own "queue_entry_cpu_time" limit rather than sharing the causer's. A connecting player's auto-look, for instance, is shown before PLAYER\`CONNECT runs.
- The enactor (%#) is the executor that caused the event. For a **system event with no executor** (an automatic dump, a signal, an idle-boot), %# is **#1 (God)**, not #-1. If the causer has been destroyed since, %# is #1 as well. Because %# is a real dbref either way, use an event's own arguments (not %#) to distinguish system- from player-caused triggers.


::: seealso
- [EVENT LIST]
- [event examples]
:::

# EVENT LIST
Event names are of the format *<type>\`<event>*. The 'type' is used simply to group similar events together for help.

Event syntax in the help is of the form:<br>
*<eventgroup>\`<eventname>* (What is passed as %0, %1, ... %9)

The following event types and events have been added to SharpMUSH. To see the help for them, type [event <type>].

Event Types:
- **dump**: dump\`5min, dump\`1min, dump\`complete, dump\`error
- **db**: db\`dbck, db\`purge, db\`warnings
- **log**: log\`err, log\`cmd, log\`conn, log\`trace, log\`check, log\`huh
- **object**: object\`create, object\`destroy, object\`move, object\`rename, object\`flag
- **sql**: sql\`connect, sql\`connectfail, sql\`disconnect
- **signal**: signal\`usr1, signal\`usr2
- **player**: player\`create, player\`connect, player\`disconnect, player\`inactivity, player\`channels
- **socket**: socket\`connect, socket\`disconnect, socket\`loginfail, socket\`createfail
- **http**: http\`blocked http\`fail http\`command
- **room**: room\`contents
- **channel**: channel\`message, channel\`who
- **page**: page\`message

The room, channel and page events, and player\`channels, are SharpMUSH's own; PennMUSH has no equivalent. They exist so a handler can send a web client structured updates (the bundled room-contents and comm-feed packages), and each names exactly who the update concerns, so a handler never has to work out again who could see what.

# EVENT DB
- **db\`dbck**: Run after the regular database consistency check.
- **db\`purge**: Run after the regular purging of destroyed objects.
- **db\`wcheck**: Run after the regular @warnings check.

**Note**: These events are only triggered after the automatic scheduled checks, and not if someone manually runs `@dbck`, `@purge` or `@wcheck`.

# EVENT DUMP
- **dump\`5min** (*Original message*, *isforking*)
- Database save will occur in 5 minutes.
- **dump\`1min** (*Original message*, *isforking*)
- Database save will occur in 1 minute.
- **dump\`complete** (*Original message*, *wasforking*)
- Database save has completed.
- **dump\`error** (*Error message*, *wasforking*, *exit_status*)
- Database save failed! You might want this to alert any admin on.
- *exit_status* has different meanings in forking and non-forking dumps.
- In forking: *exit_status* is a string, either "SIGNAL <int>" or "EXIT <int>". SIGNAL <int> refers to the mush process receiving error message via signal while EXIT <int> refers to mush process exiting abnormally.
- In nonforking: *exit_status* is "PERROR <string>" - string being the error message returned by strerror(errno). If you are seeing errors on dbsave, we recommend setting forking_dump to 0, as nonforking dumps have more verbose error messages.

The standard messages shown on dumps are still displayed when these events are set. To disable the standard message, set them to empty strings via `@config` or in mush.cnf.

# EVENT LOG
Events in the log tree get triggered whenever the game logs any information to a log file (Either because of `@log`, or something else happening.) They all get passed a single argument, the message being logged.

- **log\`err**: Errors and the general catch-all.
- **log\`cmd**: Logged commands.
- **log\`wiz**: Logged wizard activity.
- **log\`conn**: Connection notifications.
- **log\`trace**: Memory tracking notifications.
- **log\`check**: Save-releated log messages.
- **log\`huh**: Commands that generate huh messages.

# EVENT OBJECT
- **object\`create** (*new objid*, *cloned-from*)
- Triggered on the creation of any object except player. If it was created using `@clone`, then *<cloned-from>* will be a objid. Otherwise *<cloned-from>* will be null.

- **object\`destroy** (*objid*, *origname*, *type*, *owner*, *parent*, *zone*)
- Triggered _after_ the object is totally destroyed. Passed arguments are former objid, name, type, owner, etc. Enactor is always #-1, so use former owner.

- **object\`move** (*objid*, *newloc*, *origloc*, *issilent*, *cause*)
- Triggered after the object is moved, `@tel'd`, or otherwise sent to a new location. If *<issilent>* is 1, then the object was moved using `@tel/silent`.

- **object\`rename** (*objid*, *new name*, *old name*)
- Triggered when any object is renamed.
    
- **object\`flag** (*objid of object with flag*, *flag name*, *type*, *setbool*, *setstr*)
- Triggered when a flag or power which has the "event" restriction is set or cleared. *<type>* is one of FLAG or POWER. *<setbool>* is 1 if the flag/power is being set, and 0 if it's being cleared. *<setstr>* is either "SET" or "CLEARED".

### Example
```sharp
&OBJECT`FLAG #9=@cemit Admin=capstr(lcstr(%2)) %1 [lcstr(%4)] on [name(%0)] by %n.
```

# EVENT SQL
- **sql\`connect** (*platform*)
- Triggered on successful connect to the SQL database. *<platform>* is 'mysql', 'postgresql' or 'sqlite3'.

- **sql\`connectfail** (*platform*, *error message*)
- Triggered on unsuccessful connect to the SQL database.

- **sql\`disconnect** (*platform*, *error message*)
- Triggered if SQL disconnects for any reason. Usually not a worry since Penn will auto-reconnect if it can.

# EVENT SIGNAL
No arguments are passed to these events.

- **signal\`usr1**: Triggered when the SharpMUSH process receives a "kill -USR1"
- **signal\`usr2**: Triggered when the SharpMUSH process receives a "kill -USR2"

If these attributes exist, then penn will **NOT** perform what it usually does when it receives a signal. In effect, these override Penn's default actions.

To mimic old behaviour:
```sharp
&SIGNAL`USR1 #9=@nspemit/list lwho()=GAME: Reboot w/o disconnect from game account, please wait. ; @shutdown/reboot
&SIGNAL`USR2 #9=@dump
```

# EVENT PLAYER
- **player\`create** (*objid*, *name*, *how*, *descriptor*, *email*)
- Triggered when a player is created. If the player was `@pcreated`, then %# will be the person who did the `@pcreate`. If player was created by using 'create' at the connect screen, then %# will be #-1 and *<descriptor>* will be non-null. *<how>* is one of: "pcreate", "create" or "register". If created using 'register', *<email>* will be set appropriately.

- **player\`connect** (*objid*, *number of connections*, *descriptor*)
- Similar to `@aconnect`, but for events, and so you can use descriptor.

- **player\`disconnect** (*objid*, *number of remaining connections*, *hidden?*, *cause of disconnection*, *ip*, *descriptor*, *conn() secs*, *idle() secs*, *recv bytes/sent bytes/command count*)
- Similar to `@adisconnect`, but for event system, and with more information available.

- **player\`inactivity**
- Triggered when idle players are disconnected. Only run if at least one player gets idlebooted (Or auto-hidden), not at every inactivity check.

- **player\`channels** (*objid*, *cause*, *channel*)
- Triggered for a connected player when their channel list may have changed. *<cause>* is one of connect, resume, join, leave, status (a change to their own channel flags, such as a gag or a title), rename or delete; *<channel>* is the channel concerned (its new name after a rename), and empty on connect and resume. resume is a web connection that came back to its session, still logged in (a page reload, or a dropped connection), and is sent the list again. Not triggered for a player who is not connected: they are sent their whole list when they connect.

# EVENT SOCKET
- **socket\`connect** (*descriptor*, *ip*)
- Triggered when a socket first connects to the port. Using both this and player\`connect could be spammy. This happens when a connecting socket sees the connect screen.

- **socket\`disconnect** (*former descriptor*, *former ip*, *cause of disconnection*, *recv bytes/sent bytes/command count*)
- Triggered when a socket disconnects. Using this and player\`disconnect could be spammy.

- **socket\`loginfail** (*descriptor*, *IP*, *count*, *reason*, *playerobjid*, *name*)
- Triggered when a login attempt fails. *<count>* is the number of fails in the past 10 minutes. If used in conjuction with the config option connect_fail_limit, then any failures after the limit is reached will **NOT** trigger socket\`loginfail. If the connect is a failed attempt to log into a valid player, *<playerobjid>* will be set to that objid. Otherwise it will be set to #-1. *<name>* is the name the connection attempted to connect with, and is only set when *<playerobjid>* is #-1.

- **socket\`createfail** (*descriptor*, *ip*, *count*, *reason*, *name[*, *error]*)
- Triggered when a player create attempt fails. *<count>* is the # of fails caused by this ip. If the failure is from an attempt to register a player via email, the error code of the mailer program is provided as *<error>*.

**Note**: A sitelock rule with deny_silent will not trigger socket\`createfail or socket\`createfail.

# EVENT HTTP
- **http\`blocked** (*former descriptor*, *ip*, *method*, *path*, *reason*)
- Triggered when an HTTP request is sitelocked !connect, by IP or path, 'reason' will describe if it's IP or path.

- **http\`fail** (*former descriptor*, *ip*, *reason*)
- Triggered when an HTTP connection fails for poor formatting, malformed requests, or similar parsing errors. This can occur before method, path, etc are obtained, so is limited in information.

- **http\`command** (*IP*, *method*, *path*, *resp_code*, *resp_content_type*, *request_content_len*, *resp_content_len*)
- Triggered after an HTTP command is executed, using the same remaining request lifetime. It is omitted if that lifetime has already expired. If the event exhausts the deadline, the HTTP response is 503; request cancellation also cancels the event.

**Note**: A sitelock rule with deny_silent will not trigger http\`blocked

# EVENT ROOM
- **room\`contents** (*room objid*, *cause*)
- Triggered, for the room rather than for whoever moved, whenever what is in a room changes: something enters or leaves it, or a player connects or disconnects in it. *<cause>* is one of move-in, move-out, connect, disconnect or resume. %# is whoever caused it — the mover, or the wizard who `@tel`'d them. resume is a web connection that came back to its session, still logged in (a page reload, or a dropped connection): nothing in the room changed, %# is that player, and only they need the room again.

# EVENT CHANNEL
- **channel\`message** (*channel*, *speaker objid*, *style*, *speaker name*, *message*, *recipients*, *time*, *id*)
- Triggered after a channel line has been delivered. *<recipients>* is the objids of exactly the members it was delivered to: a member who has gagged the channel, one the speaker may not be heard by, one whose `@chatformat` silenced the line, and a muted member's copy of a connect or disconnect line are not among them. Nobody received it, nothing is triggered. *<style>* is one of say, pose, semipose, emit or presence (a connect or disconnect line, which only a channel with the announce privilege carries). *<speaker name>* and *<message>* are plain text, as the channel's mogrifier left them. *<speaker objid>* is empty for a line with no speaker, and both it and *<speaker name>* are empty for an `@cemit` line, which does not name its emitter (%# is still the emitter). *<time>* is milliseconds since 1970. *<id>* is the line's id, the one `@channel/recall`'s buffer holds it under (and the web portal reads it back by); ids rise with time, so a later line has a larger one.

- **channel\`who** (*channel*, *member objid*, *member name*, *on|off*, *viewers*, *cause*)
- Triggered when a member comes onto or goes off a channel's member list, as `@channel/who` lists it: a thing always, a player while connected, and a member hiding on the channel only to a viewer who may see hidden members. *<viewers>* is the objids of the connected player members whose view of the list changed, and *on* or *off* is what changed for them; a change seen differently by different members (someone starting `@channel/hide`) triggers once for each. *<cause>* is one of connect, disconnect (the last connection closing), join, leave or status (hiding or no longer hiding). %# is the member.

# EVENT PAGE
- **page\`message** (*pager objid*, *recipients*, *style*, *pager name*, *message*, *time*, *id*)
- Triggered after a page reaches at least one recipient. *<recipients>* is the objids of the players it reached: one who is not connected, is HAVEN, refuses pages from the pager, or whose page lock the pager fails is not among them. *<style>* is one of say, pose or semipose. *<pager name>* is as the recipients' terminal shows it, with the page alias when page_aliases is on. *<message>* is plain text. *<time>* is milliseconds since 1970. *<id>* is the page's id, from the sequence channel line ids come from; when the page_log option is on, it is the id the page log keeps the page under (see [page log]).

