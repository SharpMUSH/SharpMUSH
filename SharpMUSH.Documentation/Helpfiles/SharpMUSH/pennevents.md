# EVENTS

# EVENT
SharpMUSH Events are hardcoded events that may or may not be caused by players. The Event system lets administrators designate an object as an event handler (using the "event_handler" config option). The event_handler object will then have attributes triggered, with arguments, on specified events.

To use the SharpMUSH Event System:

```
> @create Event Handler
> @config/set event_handler=[num(Event Handler)]
> &<event name> Event Handler=<action list>
```

You will very likely want to set the event_handler option in your mush.cnf file to ensure it survives over dumps and is actively receiving events even during startup.

The enactor of an event is either:
1. The executor that caused it, or
2. #-1 for system events without an executor.

See also:
- [help event list|event list]
- [help event examples|event examples]

# EVENT EXAMPLES
Suppose you want random dbsave messages:
```
> &DUMP\`COMPLETE Event Handler=@config/set dump_complete=SAVE: [v(randword(lattr(me/dumpmsg\`*)))]
> &DUMPMSG\`NOTHING Event=The Database has been saved, nothing to see here.
> &DUMPMSG\`GRETZKY Event=The Database saves, but Gretzky scores!
> &DUMPMSG\`GEICO Event=The Database saved 15% by switching to Geico!
> @dump
SAVE: The Database has been saved, nothing to see here.
> @dump
SAVE: The Database saved 15% by switching to Geico!
```

Or admin want to be notified when a player connect attempt fails:
```
> @set Event=wizard
> &SOCKET\`LOGINFAIL Event=@wizwall/emit On descriptor '%0' from IP '%1' a failed connect attempt to '%4': '%3'
(Later, a player attempts to log in as #1)
Broadcast: [Event Handler]: On descriptor 3, from IP '127.0.0.1', a failed connect attempt to '#1': 'invalid password'
```

See also: [help event examples2|event examples2]

# EVENT EXAMPLES2
Suppose you want `@pcreated` players to be powered builder, set shared and zonelocked to roys, but players created at the connect screen to not be:
```
> @set Event=wizard
> &PLAYER\`CREATE Event=@assert %# ; @pemit %#=Auto-Setting [name(%0)] Builder and shared ; @power %0=builder ; @lock/zone %0=FLAG^ROYALTY ; @set %0=shared
> @pcreate Grid-BC
Auto-Setting Grid-BC Builder and Shared
```

The Event Handler object, since it's handling so many events, may become cluttered with attributes. We recommend using `@trigger` and `@include` to separate events to multiple objects.

# EVENT LIST
Event names are of the format *<type>\`<event>*. The 'type' is used simply to group similar events together for help.

Event syntax in the help is of the form:
*<eventgroup>\`<eventname>* (What is passed as %0, %1, ... %9)

The following event types and events have been added to SharpMUSH. To see the help for them, type [help event <type>|event type].

Event Types:
- **dump**: dump\`5min, dump\`1min, dump\`complete, dump\`error
- **db**: db\`dbck, db\`purge, db\`warnings
- **log**: log\`err, log\`cmd, log\`conn, log\`trace, log\`check, log\`huh
- **object**: object\`create, object\`destroy, object\`move, object\`rename, object\`flag
- **sql**: sql\`connect, sql\`connectfail, sql\`disconnect
- **signal**: signal\`usr1, signal\`usr2
- **player**: player\`create, player\`connect, player\`disconnect, player\`inactivity
- **socket**: socket\`connect, socket\`disconnect, socket\`loginfail, socket\`createfail
- **http**: http\`blocked http\`fail http\`command

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

Example:
```
&OBJECT\`FLAG event handler=@cemit Admin=capstr(lcstr(%2)) %1 [lcstr(%4)] on [name(%0)] by %n.
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
```
&SIGNAL\`USR1 Event Handler=@nspemit/list lwho()=GAME: Reboot w/o disconnect from game account, please wait. ; @shutdown/reboot
&SIGNAL\`USR2 Event Handler=@dump
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

- **http\`command** (*IP*, *method*, *path*, *resp_code*, *resp_content_type*, *resp_content_len*)
- Triggered after an HTTP command is executed.

**Note**: A sitelock rule with deny_silent will not trigger http\`blocked