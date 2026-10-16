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
- Triggered when a player types a command the game does not understand (Huh?) somewhere they do not control. *<message>* is PennMUSH's huh log line: "HUH: Name(#dbref) in Room(#dbref) \[Room's owner\]: command". %# is the player.

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

- **scene\`change** (*scene*, *key*, *previous*)
- Triggered by the Scene plugin when `@scene/set <id>/<key>=` or sceneset() changes any key of a scene but its status, which has scene\`status, and its plot, which links the scene to a plot rather than setting a key: its time (`scheduledfor`), title, room, whether it is public, or a detail. `+scene/schedule`, `/reschedule` and `/title` change keys this way. *<key>* is the key's name in lower case, and *<previous>* what it held before, empty when it was unset: a time in milliseconds since 1970, a flag as 1 or 0, a room or owner as its dbref. Read the new value with `scene(<scene>,<key>)`. Setting a key to what it already holds triggers nothing. %# is whoever changed it. Only raised while the Scene plugin is loaded.

### Example
```sharp
&SCENE`CHANGE #9=@if strmatch(%<key>,scheduledfor)={@cemit Public=Scene %<scene> now starts [convsecs(div(scene(%<scene>,scheduledfor),1000))].}
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
