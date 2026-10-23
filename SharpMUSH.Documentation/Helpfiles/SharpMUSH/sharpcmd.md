# @-attributes
These '@' commands set standard message/action sets on objects. Each comes in 3 versions: `@<whatever>`, `@o<whatever>`, and `@a<whatever>`. Only the `@<whatever>` version is listed below, but help is available for each:

|              |              |              |              |              |
|--------------|--------------|--------------|--------------|--------------|
| [@describe]  | [@drop]      | [@efail]     | [@enter]     | [@failure]   |
| [@follow]    | [@give]      | [@idescribe] | [@leave]     | [@lfail]     |
| [@move]      | [@payment]   | [@receive]   | [@success]   | [@tport]     |
| [@ufail]     | [@unfollow]  | [@use]       | [@zenter]    | [@zleave]    |


These '@' command set other standard attributes on objects that don't follow the pattern above:

|                |                |                |                |                |
|----------------|----------------|----------------|----------------|----------------|
| [@aahear]      | [@aclone]      | [@aconnect]    | [@adestroy]    | [@adisconnect] |
| [@amail]       | [@amhear]      | [@away]        | [@charges]     | [@chatformat]  |
| [@conformat]   | [@cost]        | [@descformat]  | [@ealias]      | [@exitformat]  |
| [@filter]      | [@forwardlist] | [@gender]      | [@haven]       | [@idescformat] |
| [@idle]        | [@infilter]    | [@inprefix]    | [@lalias]      | [@listen]      |
| [@nameformat]  | [@oxenter]     | [@oxleave]     | [@oxmove]      | [@oxtport]     |
| [@pageformat]  | [@prefix]      | [@runout]      | [@sex]         | [@startup]     |


::: seealso
- [attributes]
- [NON-STANDARD ATTRIBUTES]
:::

# @-building
These '@' commands are building-related (they create or modify objects):

|              |              |              |              |              |
|--------------|--------------|--------------|--------------|--------------|
| [@atrchown]  | [@atrlock]   | [@attribute] | [@chown]     | [@chzone]    |
| [@clone]     | [@cpattr]    | [@create]    | [@destroy]   | [@dig]       |
| [@elock]     | [@eunlock]   | [@firstexit] | [@link]      | [@lock]      |
| [@lset]      | [@moniker]   | [@mvattr]    | [@name]      | [@nuke]      |
| [@open]      | [@parent]    | [@recycle]   | [@set]       | [@ulock]     |
| [@undestroy] | [@unlink]    | [@unlock]    | [@uunlock]   | [@wipe]      |

# @-general
These '@' commands are general utility and programming commands:

|              |              |              |              |              |
|--------------|--------------|--------------|--------------|--------------|
| [@@]         | [@alias]     | [@break]     | [@cemit]     | [@channel]   |
| [@chat]      | [@clist]     | [@clock]     | [@command]   | [@config]    |
| [@context]   | [@decompile] | [@doing]     | [@dolist]    | [@drain]     |
| [@edit]      | [@emit]      | [@entrances] | [@find]      | [@force]     |
| [@function]  | [@gedit]     | [@grep]      | [@halt]      | [@if]        |
| [@include]   | [@input]     | [@job]       | [@lemit]     | [@list]      |
| [@listmotd]  | [@locale]    | [@mail]      | [@malias]    | [@map]       |
| [@message]   | [@notify]    | [@nsemit]    | [@nslemit]   | [@nsoemit]   |
| [@nspemit]   | [@nsprompt]  | [@nsremit]   | [@nszemit]   | [@oemit]     |
| [@password]  | [@pemit]     | [@profile]   | [@prompt]    | [@ps]        |
| [@queue]     | [@remit]     | [@restart]   | [@retry]     | [@roll]      |
| [@scan]      | [@search]    | [@select]    | [@sockset]   | [@stats]     |
| [@sweep]     | [@switch]    | [@teleport]  | [@theme]     | [@trigger]   |
| [@verb]      | [@version]   | [@wait]      | [@warnings]  | [@wcheck]    |
| [@whereis]   | [@wiki]      | [@zemit]     | [@appdraft]  |              |

# @-wizard
These '@' commands are only usable by wizards or privileged players:

|                |                |                |                |                |
|----------------|----------------|----------------|----------------|----------------|
| [@account]     | [@allhalt]     | [@allquota]    | [@ascii]       | [@backup]      |
| [@boot]        | [@chownall]    | [@chzoneall]   | [@comment]     | [@dbck]        |
| [@disable]     | [@dump]        | [@enable]      | [@feed]        | [@flag]        |
| [@hide]        | [@hook]        | [@http]        | [@kick]        | [@log]         |
| [@logwipe]     | [@mapsql]      | [@motd]        | [@newpassword] | [@package]     |
| [@pcreate]     | [@permission]  | [@plugin]      | [@poll]        | [@poor]        |
| [@power]       | [@purge]       | [@push]        | [@quota]       | [@readcache]   |
| [@reality]     | [@rejectmotd]  | [@respond]     | [@role]        | [@secret]      |
| [@shutdown]    | [@sitelock]    | [@slave]       | [@snapshot]    | [@sql]         |
| [@squota]      | [@storage]     | [@suggest]     | [@uptime]      | [@wall]        |
| [@wizmotd]     | [@wizwall]     | [cd]           | [ch]           | [cv]           |

# }
"}" is a special prefix which can be used before any command. It causes the MUSH to show debug information when evaluating that command (the same as if you had the DEBUG flag set), and for any $-commands which are triggered by the command.

In order for debug to be shown for triggered $-commands, you must either control the object(s) the matching $-commands are on, or be in the object's DEBUGFORWARDLIST attribute.


::: seealso
- [debug]
:::
# @@
`@@ [<text>]`

The "@@" command does nothing; it does not evaluate its input or show any messages to the executor. It can be used for commenting code.

Output: leaves the previous command's output unchanged.

### Example
```sharp
> @va me=$testing: @emit Test ; @@ Just a test ; @vb me=Testing
```


::: seealso
- [@@()]
- [@@()]
:::
# @aclone
`@aclone <object>=<action list>`

Sets the actions to be taken by `<object>` whenever it's @cloned. This command can be useful for notifying the owner of a vending machine or parent object when someone uses the machine.

Please note that there are no @clone or @oclone attributes.


::: seealso
- [@clone]
- [@create]
- [action lists]
:::
# @aconnect
`@aconnect <object>=<action list>`

Sets the actions to be taken by `<object>` when a player connects to the game. @aconnects are triggered on connecting players, their locations (if the room_connects @config option is true), their zone object/objects in their zone master room, and objects in the Master Room.

Note that long and spammy @aconnect messages, whether in your room or on a channel, are frequently found annoying by other players.

One argument is passed to @aconnect:<br>
%1 = number of player connections (1 if this is an initial connect)

### Example
```sharp
> @aconnect me=+who ; +bbscan
```

::: seealso
- [@adisconnect]
- [action lists]
- [EVENTS]
:::
# @amail
`@amail <object>=<action list>`

Sets the actions to be taken by `<object>` whenever it receives @mail. Admin-only, and is only triggered if enabled via the amail configuration option.


::: seealso
- [MAIL]
:::
# @adescribe
# @odescribe
`@odescribe <object>[=<message>]`<br>
`@adescribe <object>[=<action list>]`

These attributes contain the message shown to others in the enactor's location when he looks at `<object>`, and the actions to be taken by `<object>` when someone looks at it. (See [@describe] for the attribute shown to the enactor when he looks at `<object>`.) When the enactor is inside `<object>`, the @oidescribe and @aidescribe attributes will be used instead, if set. Please note that using these attributes to show long messages is often found annoying.

### Examples
```sharp
> @odescribe Walker=glances at Walker and sniggers.
> @adescribe me=think %n just looked at you.
```


::: seealso
- [look]
- [@describe]
- [@idescribe]
- [action lists]
:::
# @adestroy
`@adestroy <object>[=<action list>]`

The adestroy attribute is triggered when `<object>` is @destroyed. It can only be set by wizards. Because the attribute is triggered when `<object>` is @destroyed, not when the object is actually purged from the database, it's possible for `<object>` to be @undestroyed after the adestroy has run.

Please note that there are no destroy or odestroy attributes.


::: seealso
- [@destroy]
- [@undestroy]
- [EVENTS]
:::
# @adisconnect
`@adisconnect <object>[=<action list>]`

Sets the actions to be taken by `<object>` when a player disconnects from the game. @adisconnects are triggered on disconnecting players, their locations (if the room_connects @config option is true), their zone object/objects in their zone master room, and objects in the Master Room.

One argument is passed to @adisconnect:<br>
%1 = number of remaining connections (0 if a full disconnect)

### Example
```sharp
> @adisconnect me = home
```

::: seealso
- [@aconnect]
- [action lists]
- [RECV()]
- [SENT()]
- [CMDS()]
- [EVENTS]
:::
# @adrop
# @odrop
# @drop
`@drop <object>[=<message>]`<br>
`@odrop <object>[=<message>]`<br>
`@adrop <object>[=<action list>]`

When `<object>` is a player or thing, the @drop attribute is shown to whoever drops `<object>`, and @odrop to others in the location `<object>` is dropped in. The @adrop attribute is triggered when `<object>` is dropped.

When `<object>` is an exit, @drop is shown to objects going through `<object>`, and @odrop is shown to objects in the exit's destination. @adrop is triggered when someone passes through the exit.

### Example
```sharp
> @drop Box=You put the box down gently.
> @odrop Box=puts the box down gently.
```

```sharp
> @odrop South=arrives from the North.
```

::: seealso
- [drop]
- [empty]
- [action lists]
- [verbs]
- [@asuccess]
:::
# @aefail
# @oefail
# @efail
`@efail <object>[=<message>]`<br>
`@oefail <object>[=<message>]`<br>
`@aefail <object>[=<action list>]`

These attributes contain the message shown to someone who fails to enter `<object>`, the message shown to others when someone fails to enter `<object>`, and the actions to be taken when someone fails to enter it, respectively.


::: seealso
- [enter]
- [@aenter]
- [failure]
- [action lists]
- [verbs]
:::
# @aufail
# @oufail
# @ufail
`@ufail <object>=[<message>]`<br>
`@oufail <object>=[<message>]`<br>
`@aufail <object>=[<action list>]`

Sets the message shown to a player who fails to use an object via the 'use' command (because they don't pass the @lock/use), the message shown to others in the room when a player fails to use `<object>`, and the actions to be taken by `<object>` when someone fails to use it, respectively.

Note that these attributes are @ufail, NOT @ufailure, for TinyMUSH compatibility.

Although the Use @lock also restricts who can trigger $-commands or ^-listens on an object, these attributes will not be triggered for those failures. Instead, the COMMAND_LOCK`* and LISTEN_LOCK`* attributes are triggered. See [failure] for more information.

::: seealso
- [use]
- [@ause]
- [failure]
- [action lists]
- [verbs]
:::
# @afailure
# @ofailure
# @failure
`@failure <object>[=<message>]`<br>
`@ofailure <object>[=<message>]`<br>
`@afailure <object>[=<action list>]`

@failure contains the message shown to someone who fails to pass `<object>`'s Basic @lock. @ofailure contains the message shown to others, and @afailure contains the actions to be taken by `<object>`.

For players and things, this means failure to get/take. For exits, it means failure to go through the exit. For rooms the lock is checked when objects "look" inside the room, though failure to pass the lock does not prevent the object from looking.


::: seealso
- [get]
- [go]
- [LOCKING]
- [action lists]
- [verbs]
- [@asuccess]
:::
# @follow
# @ofollow
# @afollow
`@follow <object>[=<message>]`<br>
`@ofollow <object>[=<message>]`<br>
`@afollow <object>[=<action list>]`

Sets the message shown to someone who begins following `<object>`, the message shown to others in the room, and the actions to be taken by `<object>` when someone begins following it, respectively. The name of the person following `<object>` is automatically prepended to the @ofollow message.

::: seealso
- [follow]
- [unfollow]
- [@unfollow]
- [FOLLOWERS()]
- [action lists]
- [verbs]
:::
# @unfollow
# @ounfollow
# @aunfollow
`@unfollow <object>[=<message>]`<br>
`@ounfollow <object>[=<message>]`<br>
`@aunfollow <object>[=<action list>]`

Sets the message shown to someone who stops following `<object>` with "unfollow `<object>`", the message shown to others in the room, and the actions to be taken by `<object>` when someone stops following it, respectively. The name of the person stopping following `<object>` is automatically prepended to the @ounfollow message.

::: seealso
- [follow]
- [unfollow]
- [@follow]
- [FOLLOWERS()]
- [action lists]
- [verbs]
:::
# @ahear
# @amhear
# @aahear
`@ahear <object>[=<action list>]`<br>
`@amhear <object>[=<action list>]`<br>
`@aahear <object>[=<action list>]`

Sets the actions to be taken after the object's @listen is matched. @ahear will only be triggered by sound made by other objects, and @amhear is only triggered by sound made by `<object>` itself. @aahear will be triggered by all matching sound, regardless of the source.


::: seealso
- [@listen]
- [listening]
- [action lists]
:::
# @leave
# @oleave
# @oxleave
# @aleave
`@leave <object>[=<message>]`<br>
`@oleave <object>[=<message>]`<br>
`@oxleave <object>[=<message>]`<br>
`@aleave <object>[=<action list>]`

These attributes contain the message shown to anyone leaving `<object>`, the message shown to others inside `<object>` when someone leaves it, the message shown to others in `<object>`'s location when someone leaves it, and the actions to be taken by `<object>` when someone leaves it, respectively.

The leaver's new location is passed in %0, if `<object>` has permission to see it there.


::: seealso
- [leave]
- [@leave]
- [@lfail]
- [action lists]
- [verbs]
:::
# @lfail
# @olfail
# @alfail
`@lfail <object>[=<message>]`<br>
`@olfail <object>[=<message>]`<br>
`@alfail <object>[=<action list>]`

These attributes contain the message shown to objects who try to leave `<object>` and fail, the message shown to others inside `<object>` when someone fails to leave, and the actions to be taken by `<object>` when someone attempts to leave it and fails.

Such a failure usually occurs because `<object>` is set NO_LEAVE, or because the person trying to leave does not pass `<object>`'s @lock/leave.


::: seealso
- [leave]
- [@leave]
- [locktypes]
- [action lists]
- [verbs]
:::
# @alias
`@alias <player>[=<name1>[;<name2>[;...;<nameN>]]]`<br>
`@alias <object>[=<string>]`

For players and exits, the ALIAS attribute has special meaning: it contains a list of aliases (separated by semicolons) which can be used instead of its name to refer to the player or exit.

Players can only have a limited number of aliases; the number is controlled by the 'max_aliases' @config option. The same rules which apply to player names also apply to aliases, and you cannot use another player's name as your alias (though you can include your own name in your aliases, and can change your name to one of your aliases).

If the 'page_aliases' @config option is on, the first alias in the list is shown along with the player's name when they page others.

Exit aliases used to be a part of their name, though all newly created exits use @alias instead.

For other types of object, @alias has no special meaning.


::: seealso
- [@name]
- [ALIAS()]
- [ALIAS()]
:::
# @move
# @omove
# @oxmove
# @amove
`@move <object>[=<message>]`<br>
`@omove <object>[=<message>]`<br>
`@oxmove <object>[=<message>]`<br>
`@amove <object>[=<action list>]`

These attributes contain the message shown to `<object>` immediately after it moves, the message shown to others in the room `<object>` moves into, the message shown to objects in the location `<object>` leaves, and the actions to be taken when `<object>` moves, respectively. Please note that long @omoves are frequently found annoying.

The `<object>`'s new location is in %0 and the old location it moved from in %1.

### Example
```sharp
> @move me=You moved! You are now in the room: [name(here)].
> @omove me=stalks into the room wearing a malevolent expression.
> @oxmove me=stalks away, glaring.
```


::: seealso
- [go]
- [@move]
- [action lists]
- [verbs]
:::
# @aenter
# @enter
# @oenter
# @oxenter
`@enter <object>[=<message>]`<br>
`@oenter <object>[=<message>]`<br>
`@oxenter <object>[=<message>]`<br>
`@aenter <object>[=<action list>]`

These attributes contain the messages shown to someone who enters `<object>`, the message shown to others inside `<object>` when someone enters it, the message shown to those in `<object>`'s location when someone enters it, and the actions to be taken by `<object>` when someone enters it, respectively.

The old location of the entering object is passed in %0, if `<object>` had permission to see it there.

### Example
```sharp
> @enter Sofa=You sit on the comfy sofa.
> @oenter Sofa=sits with you on the sofa.
> @oxenter Sofa=sits down on the sofa. It looks comfy.
> @aenter Sofa=@pemit/silent owner(me)=%n sat down on [name(me)]!
```


::: seealso
- [enter]
- [@ealias]
- [leave]
- [action lists]
- [verbs]
:::
# @apayment
# @payment
# @opayment
`@payment <object>[=<message>]`<br>
`@opayment <object>[=<message>]`<br>
`@apayment <object>[=<action list>]`

These attributes can be set, but SharpMUSH has no money: "give" never pays an object, so they are never shown or run.

### Example
```sharp
> @payment Collecting Tin=Thank you for your donation!
> @opayment Collecting Tin=makes a donation to charity.
> @apayment Collecting Tin=&%# me=%0 at [time()]
```


::: seealso
- [give]
- [@cost]
- [buy]
- [MONEY]
- [action lists]
- [verbs]
:::
# @atport
# @tport
# @otport
# @oxtport
`@tport <object>[=<message>]`<br>
`@otport <object>[=<message>]`<br>
`@oxtport <object> [=<message>]`<br>
`@atport <object>[=<action list>]`

These attributes contain the message shown to `<object>` when it is teleported, the message shown to others in the room `<object>` is teleported to, the message shown to others in the room `<object>` is teleported from, and the actions to be taken by `<object>` when it disappears, respectively.

In all of these attributes, %0 is the object which teleported `<object>`, and %1 is `<object>`'s old location.

### Example
```sharp
> @tport me=name(%0) has teleported you from [name(%1)] to [name(here)].
> @otport me=appears in a puff of smoke.
> @oxtport me=disappears in a puff of smoke.
```


::: seealso
- [@teleport]
- [action lists]
- [verbs]
:::
# @atrchown
# @attrchown
`@atrchown <object>/<attribute>=<new owner>`

This command changes the ownership of the attribute `<attribute>` on `<object>` to `<new owner>`. You can only @atrchown attributes which you can set. Wizards can @atrchown to any player, while mortals can only @atrchown attributes to themselves. Only players can own attributes; if `<new owner>` is not a player, `<new owner>`'s owner is used instead.

`<attribute>` may be a wildcard pattern (see [wildcards]). Then every attribute of `<object>`'s own that matches and that you can see changes owner, and each one is reported by name. PennMUSH takes one attribute at a time; patterns are a SharpMUSH addition.
```sharp
> @atrchown Widget/CMD`**=me
```
Output: one "Attribute <name> owner changed." line for each attribute under CMD on Widget.


::: seealso
- [@atrlock]
- [@chown]
- [OWNER()]
- [attributes]
- [NON-STANDARD ATTRIBUTES]
:::
# @atrlock
# @attrlock
`@atrlock <object>/<attribute>`<br>
`@atrlock <object>/<attribute>=[on|off]`

The first form of this command tells you whether or not the given attribute is locked (whether it has the "locked" attribute flag set).

The second form attempts to lock (for 'on') or unlock (for 'off') the given attribute. You automatically gain ownership of the attribute (as per @atrchown) when you lock it. Locked attributes cannot be altered by anyone but Wizards and the attribute's owner (though the owner may be unable to alter the attribute for other reasons, such as not controlling `<object>`). You must be able to set an attribute in order to lock it.

If you wish to lock an attribute without gaining ownership, you can set it "locked" with `@set <obj>/<attr>=locked` - be aware that you'll be unable to make any changes to the attribute after this, including unlocking it!

`<attribute>` may be a wildcard pattern (see [wildcards]). Then both forms act on every attribute of `<object>`'s own that matches and that you can see, and each line names its attribute. PennMUSH takes one attribute at a time; patterns are a SharpMUSH addition.
```sharp
> @atrlock Widget/CMD`*=on
```
Output: one "Attribute <name> locked." line for each attribute directly under CMD on Widget.


::: seealso
- [ATRLOCK()]
- [@atrchown]
- [attributes]
- [NON-STANDARD ATTRIBUTES]
:::
# @asuccess
# @success
# @osuccess
`@success <object>[=<message>]`<br>
`@osuccess <object>[=<message>]`<br>
`@asuccess <object>[=<action list>]`

For players and things, these attributes contain the message shown to someone who picks up `<object>` with the "get" command, the message shown to others when someone gets `<object>`, and the actions to be taken by `<object>` when someone gets it, respectively.

For exits, they contain the message shown to an object passing through the exit `<object>`, the message shown in the exit's source when someone passes through it, and the actions to be taken by the exit when someone passes through it, respectively.

In all cases, %0 is the dbref of the moving object's original location.

### Example
```sharp
> @success Door=You open the door and step inside.
> @osuccess Door=opens the door and steps inside.
```

```sharp
> @success Box=You pick up the box.
> @osuccess Box=picks up the box.
```


::: seealso
- [get]
- [go]
- [LOCKING]
- [SUCCESS]
- [failure]
- [@adrop]
- [action lists]
- [verbs]
:::
# @ause
# @use
# @ouse
`@use <object>[=<message>]`<br>
`@ouse <object>[=<message>]`<br>
`@ause <object>[=<action list>]`

These attributes contain the message shown to someone who successfully uses `<object>`, the message shown to others when someone uses `<object>`, and the actions to be taken by `<object>` when it is used, respectively.

Note that, if `<object>` has a CHARGES attribute set and it does not contain a number greater than 0, the RUNOUT attribute is triggered instead of the AUSE attribute. See [@charges] for more information.

### Example
```sharp
> @use Jack-In-The-Box=You wind the handle.
> @ouse Jack-In-The-Box=winds the handle.
> @ause Jack-In-The-Box=@wait 3=POSE pops up with a bang!
> use Jack-In-The-Box
```


::: seealso
- [use]
- [@charges]
- [@charges]
- [action lists]
- [verbs]
:::
# @away
`@away <player>[=<message>]`

If `<message>` evaluates to something non-null, it is shown to anyone who pages `<player>` while `<player>` is not connected, in place of "`<player>` is not connected."

### Example
```sharp
> @away me=I'm not here, please send me @mail instead.
```


::: seealso
- [@idle]
- [@haven]
:::
# @boot
`@boot[/silent] <player>`<br>
`@boot/port[/silent] <descriptor number>`<br>
`@boot/me`

The first form of this command disconnects `<player>`'s most recent connection from the game.

The `/port` switch disconnects a particular descriptor (as shown under "Des" in the Wizard WHO, returned by lports() and ports(), etc).

If the `/silent` switch is given, the message telling `<player>` he was booted is suppressed.

The `/me` switch disconnects the connection you typed the command on.

Anyone may @boot their own connections. Only holders of `players.moderate` and those with the "boot" power can @boot other players, and each time they do it is recorded in the audit log.


::: seealso
- [QUIT]
- [LOGOUT]
:::
# @chown
`@chown[/preserve] <object>=<player>`

Changes the ownership of `<object>` to `<player>`. You can chown things, rooms or exits. Players can't be @chown'd - they always own themselves. To chown a thing, you have to be carrying it. If you do not own an object, you can only chown it if it has the CHOWN_OK flag and you pass its @lock/chown. If you're not a Wizard, you can only @chown objects to yourself or a Zone Master whose zone-lock you pass.

Normally, @chown'ing an object clears privileged flags and powers, and sets the object halt. Wizards can use `@chown/preserve` to avoid this. Doing this to an active object with queued commands is not recommended, and may have strange and insecure effects.

To change the owner of an attribute, use [@atrchown].

### Examples
```sharp
> @chown here=me (for a room)
> @chown box=Soundwave (for a thing)
```


::: seealso
- [CHOWN_OK]
- [zone masters]
- [@chownall]
- [OWNER()]
- [@atrchown]
:::
# @chownall
`@chownall[/preserve][/<types>] <player>[=<new owner>]`

This command @chowns all objects currently owned by `<player>` to the player `<new owner>`, or to the person running the command if no `<new owner>` is given. All the objects will have privileged flags and powers cleared, and be set halt, unless the `/preserve` switch is given.

If one or more of `/things`, `/rooms` or `/exits` are provided, only objects of the given types are chowned. Otherwise, all objects are chowned.

This command can only be used by Wizards.


::: seealso
- [@chown]
:::
# @chzoneall
`@chzoneall[/preserve] <player>=<zone object>`

Changes the zone of all objects owned by `<player>` to `<zone object>`. If `<zone object>` is "none", the zone is reset to NOTHING. Only wizards may use this command.


::: seealso
- [@chzone]
- [zones]
:::
# @comment
`@comment <object>[=<comment>]`

This is a wizard-only command which sets a COMMENT attribute on `<object>`. The attribute can only be seen by those with the See_All power.


::: seealso
- [@@]
- [@@()]
:::
# @conformat
`@conformat <object>[=<format>]`

When set, the CONFORMAT attribute is evaluated when `<object>` is looked at, and the result is displayed instead of the usual "Contents:" (for rooms) or "Carrying:" (for players and things) list of contents.

The dbrefs of the objects which would appear in the normal contents list are passed to the attribute as %0 or %`\<contents\>` (you can use lcon(me) for a full contents list). A list of the names of these objects is passed as %1 or %`\<names\>`, |-delimited. Each is passed by position and by name (see [named arguments]).

Q-registers (set via setq() and similar functions) are inherited from the @descformat, and passed on to the @exitformat.

### Examples
Show the normal contents list, but in upper-case:
```sharp
> @conformat here=edit(ucstr(%1), |, %r)
```

Show just the object names (with no ansi) in a table:
```sharp
> @conformat here=table(iter(%<contents>, name(%i0), %b, |), 20, width(%#), |)
```


::: seealso
- [look]
- [@exitformat]
- [@nameformat]
- [@descformat]
- [@invformat]
- [@idescformat]
:::

# @invformat
`@invformat <object>[=<format>]`

When set, this attribute is evaluated and displayed instead of the usual "You are carrying:" list of objects when `<object>` uses the "inventory" command. The dbrefs of the objects that would appear in the inventory are passed as %0 or %`\<contents\>`, and a list of their names as they would appear in the usual list, |-delimited, as %1 or %`\<names\>` (see [named arguments]). If the result is empty, nothing is shown.

### Example
```sharp
> @invformat me=You're holding: [itemize(iter(%0, name(%i0), %b, |), |)]
> inventory
You're holding: Red Ball, Pickle, and Piano
```


::: seealso
- [inventory]
- [@conformat]
- [@exitformat]
- [@nameformat]
- [@descformat]
- [@idescformat]
:::
# @descformat
`@descformat <object>[=<format>]`

When set, this attribute is evaluated and displayed instead of `<object>`'s @describe, when someone looks at `<object>`. The evaluated @describe, which would be shown if no @descformat were set, is passed to the @descformat as %0, and by name as %`\<description\>` (see [named arguments]); use v(describe) to get the unevaluated description.

This is primarily useful for room parents, to enforce a consistent look for all rooms without having to apply formatting to ever @describe. Note that this object is only used with @describe - to format the @idescribe, use @idescformat.

Q-registers (set via setq() and similar functions) are inherited from the @nameformat, and passed on to the @conformat.

### Example
```sharp
> @descformat Room Parent=repeat(=, width(%#))%r%0[repeat(=, width(%#))]
```


::: seealso
- [look]
- [@exitformat]
- [@nameformat]
- [@conformat]
- [@idescformat]
- [@invformat]
:::
# @idescformat
`@idescformat <object>[=<format>]`

When set, this attribute is evaluated and displayed instead of `<object>`'s inside description when someone looks at `<object>` while inside it. The evaluated @idescribe is passed to @idescformat as %0, and by name as %`\<description\>` (see [named arguments]); use v(idescribe) to get the unevaluated description.

If no @idescribe is set, @idescformat formats the evaluated @describe instead. When neither @idescribe nor @idescformat is set, the normal @describe and @descformat attributes are used.

This is useful for things like object parents that enforce a consistent "look" for each object's @idescribe, without having to place formatting into every @idescribe.

Q-registers (set via setq() and similar functions) are inherited from the @nameformat, and passed on to the @conformat.

### Example
```sharp
> @idescribe Vehicle Parent=repeat(*, width(%#))%r%0
```


::: seealso
- [look]
- [@exitformat]
- [@nameformat]
- [@conformat]
- [@descformat]
- [@invformat]
:::
# @nameaccent
`@nameaccent <object>[=<accent template>]`

When this attribute holds an accent template that is the same length as `<object>`'s @name, it is used to change the object's name in some situations (how it shows up in speech, look, and a few other commands). This allows for accented names without having to use the accented characters directly in a name, which can make it harder for people to type.

The `<accent template>` is explained in [accents].

If a container has both a @nameaccent and a @nameformat, the @nameformat is used.


::: seealso
- [accent()]
- [@nameformat]
- [ACCNAME()]
- [STRIPACCENTS()]
- [INAME()]
:::
# @nameformat
`@nameformat <object>[=<format>]`

When set, this attribute is evaluated and displayed in place of `<object>`'s name, when objects inside `<object>` "look". The room's dbref is passed as %0 or %`\<object\>`, and the default-formatted name (as it would be displayed with no @nameformat set) is passed as %1 or %`\<name\>`. Each is passed by position and by name (see [named arguments]).

@nameformat is not used when people who are outside the object look at it.

Q-registers (set via setq() and similar functions) are passed on from the nameformat to the other @*format attributes used for formatting "look" output. Use localize() if you don't want this behaviour.

### Example
Show the room's zone after its name.
```sharp
> @nameformat here = %1 [if(zone(%0),<[name(zone(%0))]>)]
```

The same, reading the arguments by name:
```sharp
> @nameformat here = %<name> [if(zone(%<object>),<[name(zone(%<object>))]>)]
```


::: seealso
- [look]
- [@exitformat]
- [@conformat]
- [@descformat]
- [@nameaccent]
- [@invformat]
- [@idescformat]
- [INAME()]
:::
# @cost
`@cost <object>[=<amount>]`

The COST attribute can be set, but SharpMUSH has no money: "give" with a number only answers "Money transfer will not be implemented.", so COST is never read and @payment, @opayment and @apayment never run.


::: seealso
- [give]
- [MONEY]
- [@payment]
- [MONEY()]
- [buy]
:::
# @create
`@create <name>[=<cost>[,<dbref>]]`

This command creates a new thing called `<name>`. SharpMUSH has no money, so creating an object costs nothing and `<cost>` is accepted and ignored.

Output: the new thing's dbref, as `create()` returns it.

When quotas are enabled, each object you create uses part of your building quota; see [@quota].

Wizards and objects with the PICK_DBREFS power can also specify the `<dbref>` of a garbage object to use when creating the object. Otherwise, the object is given the next available dbref.


::: seealso
- [give]
- [@quota]
- [MONEY]
- [@clone]
- [CREATE()]
- [@dig]
- [@open]
- [@pcreate]
:::
# @dbck
`@dbck`

Wizard-only. Reads every entry in every table of the newest backup of the world, and reports how
much it read. A copy LMDB cannot read fails the check now, rather than on the day you restore it.
The copy is opened read-only and never changed. A check that passes raises db\`dbck.

PennMUSH's `@dbck` checks the live database's links. SharpMUSH keeps its links consistent as it
writes them, so its `@dbck` checks the copy you would restore instead, and runs only when asked. Take
a copy with `@backup` first.

::: seealso
- [@backup]
- [event db]
:::
# @describe
# @desc
`@describe <object>[=<description>]`

This command sets the description of the object, which will be seen whenever something looks at the object with the 'look' command. Every object should have a description, even if just a short one describing its purpose. When looking at a thing, player or exit which has no description, you will see the message "You see nothing special.". A room with no desc set shows nothing.

The description can be formatted using the @descformat attribute. This is particularly useful for @parents and ancestors.

When inside a thing or player, you will see its @idescribe instead, if one is set.

@describe can be abbreviated as @desc.


::: seealso
- [look]
- [@adescribe]
- [@idescribe]
- [@descformat]
:::
# @undestroy
# @unrecycle
`@undestroy <object>`

When an object has been marked for destruction using @destroy, this command spares it from destruction, removing the GOING and GOING_TWICE flags. You must control `<object>`. `<object>`'s @startup is triggered when it is spared.

If `<object>`'s owner is marked for destruction, they will also be spared.

If `<object>` is a player and the 'destroy_possessions' @config option is on, all objects he owns will also be undestroyed. If `<object>` is a room, all exits in the room will also be undestroyed. If `<object>` is an exit and its source room is marked for destruction, it will be undestroyed.

@unrecycle is an alias for @undestroy.


::: seealso
- [@destroy]
- [GOING]
- [@startup]
:::
# @doing
`@doing <object>[=<message>]`

With a `<message>` argument, this command sets `<object>`'s DOING attribute to `<message>`. The DOING attribute is only meaningful for players; the evaluated result is shown on the output of the normal MUSH WHO command, and on the login screen WHO.

With no `<message>` the attribute is cleared.

To change the message shown above player @doings in WHO, use @poll.


::: seealso
- [@poll]
- [who]
- [DOING()]
:::
# @drain
`@drain[/any][/all] <object>[/<attribute>][=<number>]`

This command discards commands waiting on a semaphore without executing them. (For non-semaphore queues, use @halt or @halt/pid.)

Waiting commands are discarded only after the corresponding counter change is confirmed. An uncertain database result retains the waiting entries for reconciliation, using the same recovery behavior as [@notify].

If the `/any` switch is given, then all semaphores associated with `<object>` are @drained. Otherwise, only the specified semaphore attribute (or SEMAPHORE if no `<attribute>` is specified) is @drained.

If the `/all` switch is given, then all queue entries associated with the selected semaphore(s) are discarded, and the semaphore attribute(s) are cleared. Otherwise, only the indicated `<number>` of queue entries are discarded. If no `<number>` is given, then the `/all` switch is assumed.

You may not specify both the `/any` switch and a specific attribute. Similarly, you may not specify both the `/all` switch and a number.


::: seealso
- [semaphores]
- [@wait]
- [@notify]
- [@halt]
:::
# @dump
`@dump`<br>
`@dump[/paranoid|/debug|/nofork] [<check interval>]`

Wizard-only. Takes a backup of the world, as `@backup` does. In PennMUSH, `@dump` writes the database
from memory to disk; a SharpMUSH game commits every change as it is made, so the game on disk is
already current and a copy of it is the dump. The switches and check interval are accepted for
PennMUSH code and ignored.

Everyone sees dump_message as the copy starts and dump_complete when it is written, and the dump
events are raised (see [event dump]). Scheduled copies run every SHARPMUSH_BACKUP_INTERVAL, a server
setting, rather than PennMUSH's dump_interval.


::: seealso
- [@backup]
- [@shutdown]
:::
# @ealias
# @lalias
`@ealias <object>[=<enter alias1>[; ... ; <enter aliasN>]]`<br>
`@lalias <object>[=<leave alias1>[; ... ; <leave aliasN>]]`

These attributes contain lists of "enter aliases" and "leave aliases" for `<object>` - any of the aliases can be used in place of "enter `<object>`" and "leave `<object>`" to enter/leave the object.

These attributes only have meaning for players and things (as rooms/exits cannot be "enter"ed) - the aliases for exits are stored in @alias.

### Example
```sharp
> @ealias Chair=Sit down;sit
> @lalias Chair=Stand up;stand
```


::: seealso
- [enter]
- [leave]
- [go]
- [ENTER_OK]
:::
# @elock
# @eunlock
`@elock <object>[=<key>]`<br>
`@eunlock <object>`

@elock sets the Enter @lock for `<object>` to `<key>`, or clears the the lock if no `<key>`. is given. @eunlock removes the Enter @lock for `<object>`.

@elock and @eunlock are deprecated and uses of them should be replaced by<br>
`@lock/enter <object>[=<key>]`<br>
and<br>
`@lock/enter <object>`


::: seealso
- [LOCKING]
- [locktypes]
- [enter]
- [ENTER_OK]
:::
# @emit
# \
`@emit[/<switch>] <message>`<br>
`\<message>`

This sends `<message>` to everyone in your location. Nothing is added to the message, not even your name. If you have a SPEECHMOD attribute set, it will be evaluated with `<message>` as %0 or %`\<message\>`, and | as %1 or %`\<type\>`, and the result will be shown instead of `<message>` as long as it evaluates to a non-empty string.

The `/noeval` switch prevents the MUSH from evaluating `<message>`. The `/spoof` switch causes nospoof notifications to show the enactor's dbref instead of the executor's dbref, and requires control over the enactor or the Can_spoof power.

@emit can be abbreviated as `\`


::: seealso
- [@nspemit]
- [EMIT()]
- [@pemit]
- [@remit]
- [@oemit]
- [@lemit]
- [@zemit]
- [@CEMIT]
- [@SPEECHMOD]
- [NOSPOOF]
- [SPOOFING]
:::
# @enable
# @disable
`@enable <option>`<br>
`@disable <option>`

These wizard-only commands change any boolean @config option (see [@config parameters] for a list), answering "Enabled." or "Disabled.". An option that is not on/off, or that @config/set cannot change, is refused.

`@enable <option>` is the same thing as `@config/set <option>=yes`<br>
`@disable <option>` is the same thing as `@config/set <option>=no`

Like @config/set, the change is stored and lasts across restarts.


::: seealso
- [@config]
:::
# @zenter
# @ozenter
# @azenter
`@zenter <object>[=<message>]`<br>
`@ozenter <object>[=<message>]`<br>
`@azenter <object>[=<action list>]`

These attributes set the message shown to a player when he enters the zone `<object>`, the message shown to others in the room the player enters when he enters the zone, and the action to be taken by the zone `<object>` when the player moves into an area zoned to it.

Entry into a new zone is said to occur when a player goes from a room not in the zone to a room in the zone. "Room" in this context means the player's absolute room (outermost container), so entering and leaving unzoned objects within a zoned room doesn't trigger these.

Zone entry is assumed to occur before room entry, so these are triggered before the room's `@[oa]enter`.


::: seealso
- [@zleave]
- [zones]
- [@zemit]
- [ZWHO()]
- [verbs]
:::
# @zleave
# @ozleave
# @azleave
`@zleave <object>[=<message>]`<br>
`@ozleave <object>[=<message>]`<br>
`@azleave <object>[=<action list>]`

These attributes set the message shown to a player when he leaves the zone `<object>`, the message shown to others in the room he left when leaving the zone, and the actions to be taken by `<object>` with a player leaves an area zoned to it.

Leaving a zone is said to occur when a player goes from a room in the zone to a room not in the zone. "Room" in this context means the player's absolute room (outermost container), so entering and leaving unzoned objects within a zoned room doesn't trigger these.

Zone leaving is assumed to occur after room leaving, so these are triggered after the room's `@[oa]leave`.


::: seealso
- [@zenter]
- [zones]
- [@zemit]
- [ZWHO()]
- [verbs]
:::
# @entrances
`@entrances[/<switch>] [<object>][=<begin>[, <end>]]`

This command will show you all objects linked to `<object>`. If you don't specify an `<object>`, your current location is used. You can limit the range of the dbrefs searched by specifying `<begin>` and `<end>`.

Output: the dbrefs of the entrances found, as `entrances()` returns them.

You can use any combination of switches to limit the types of objects:
- /exits show only exits linked to `<object>`
- /things show only things which have their homes in `<object>`
- /players show only players who have their homes in `<object>`
- /rooms show only rooms which have a drop-to of `<object>`

If you control `<object>`, or have the Search or See_All powers, all objects linked to `<object>` are listed. Otherwise, only objects which you can examine will be shown.


::: seealso
- [@link]
- [@search]
- [ENTRANCES()]
:::
# @exitformat
`@exitformat <object>[=<format>]`

When set, the exitformat attribute is evaluated and shown in place of the "Obvious exits" list for a room. (It has no meaning when set on other types of object.) The dbrefs of the exits which would appear in the default "Obvious exits" list is passed to the attribute as %0, and by name as %`\<exits\>` (see [named arguments]), as a space-separated list; you can use lexits(me) to get all the exits in the room.

Q-registers (set via setq() and similar functions) are inherited from the @conformat.

### Example
```sharp
> @exitformat here=Exits: [itemize(iter(%0, name(%i0)))]
```

The same, reading the exits by name:
```sharp
> @exitformat here=Exits: [itemize(iter(%<exits>, name(%i0)))]
```


::: seealso
- [TRANSPARENT]
- [@conformat]
- [@nameformat]
- [@descformat]
:::
# @firstexit
`@firstexit <exit1>[, ... , <exitN>]`

Normally, exits appear in a room's Obvious exits list in the order they were created, most recent first. You can use this command to rearrange them. @firstexit moves each exit, in the order given, to the top of the Obvious exits list for its source room. You must control the room.

### Example
```sharp
> @dig/teleport Test Room
> @open Two ; @open Three ; @open One
> look
Test Room(#3Rn)
Obvious exits:
One, Three, and Two
> @firstexit two, one
> look
Test Room(#3Rn)
Obvious exits:
One, Two, and Three
```


::: seealso
- [EXITS]
- [@open]
- [@link]
:::
# @find
`@find [<name>][=<begin>, <end>]`

Displays the name and dbref of every room, thing, or player you control whose name matches `<name>`. If `<begin>` and `<end>` are given, @find will start at the `<begin>`th object in the database, and will not search past the `<end>`th object.

Output: the dbrefs found, separated by spaces.

You may wish to use the @search command instead, which can filter the results more complexly.


::: seealso
- [@search]
- [lsearch()]
- [@entrances]
:::
# @forwardlist
# FORWARDLIST
`@forwardlist <object>[=<list of dbrefs>]`

If `<object>` is set AUDIBLE, any sound it hears which passes its @filter and @lock/filter is forwarded (prefixed with its @prefix) to each of the dbrefs given in its @forwardlist attribute, in much the same way as puppets forward sound to their owners. A room in the list passes the sound to everything in it.

In order to forward to an object, you must either control it, have the pemit_all power, or pass its @lock/forward. (If you want to allow all objects you own to forward to you, regardless of whether or not they control you, use `@lock/forward me=$me`)


::: seealso
- [@filter]
- [@prefix]
- [AUDIBLE]
- [PUPPET]
- [@debugforwardlist]
- [LOCKING]
:::
# @debugforwardlist
# DEBUGFORWARDLIST
`@debugforwardlist <object>[=<list of dbrefs>]`

When `<object>` has an @debugforwardlist attribute set, any debug output it produces (either because it has the DEBUG flag set, or because an attribute with the DEBUG attribute flag is evaluated) is forwarded to all the dbrefs listed in the debugforwardlist.

The @debugforwardlist must be a space-seperated list of dbrefs. In order to forward to an object, you must either control it, have the pemit_all power, or pass its @lock/forward.


::: seealso
- [debug]
- [@forwardlist]
- [LOCKING]
:::
# Flag Permissions
The following permissions can be used when specifying whether `<actor>` may set or clear a flag on an `<object>` they control:

- trusted `<actor>` must pass a TRUST check (see help TRUST)
- royalty `<actor>` must be ROYALTY or WIZARD
- wizard `<actor>` must be WIZARD
- god `<actor>` must be God (#1)

The following permissions can be used to specify whether `<looker>` can see the flag on an `<object>`, and are given along with the `<setperms>` in `@flag/add`. By default, anyone can see the flag:

- dark `<actor>` must be Only God (#1) to see the flag on objects
- mdark `<actor>` must be WIZARD or ROYALTY
- odark `<actor>` must own the `<object>` (or be WIZARD or ROYALTY)

The following permissions control other behavior related to the flag:

log Log when the flag is set or cleared. Only meaningful in `<setperms>`.<br>
event Trigger the ``OBJECT`FLAG`` event when this flag is set or cleared. Only meaningful in `<setperms>`. See [EVENTS] for more information.
# @grep
`@grep[/<switches>] <object>[/<attrs>]=<pattern>`

@grep returns a list of all attributes on `<object>` which match `<pattern>`. If `<attrs>` is specified, only attributes which match the wildcard pattern `<attrs>` are checked; it defaults to "*". Use "**" for all attributes.

Output: the names of the attributes matched, as `grep()` returns them.

By default, attributes which contain the string `<pattern>` are returned. However, if the `/wild` switch is given, `<pattern>` is treated as a wildcard pattern, and attributes which match the pattern are returned. If the `/regexp` switch is given, `<pattern>` is treated as a regular expression, and attributes matching the regexp are returned. Please note that `<pattern>` will NOT be evaluated, so you can easily grep for code strings.

All matches are case-sensitive, unless the `/nocase` switch is given.

@grep only shows a list of matching attributes. However, you can specify the `/print` switch, in which case attribute values are also shown, with the matching substrings ansi-highlighted if appropriate.

If the `/parent` switch is given, attributes `<object>` inherits from its parent(s) will be checked as well.

For backwards compatability, the `/list` switch provides the default behaviour of listing attributes without printing the values, and `/ilist` and `/iprint` are aliases for `/list/nocase` and `/print/nocase`.


::: seealso
- [GREP()]
- [GREP()]
- [GREP()]
- [WILDCARDS]
:::
# @haven
`@haven <player>[=<message>]`

When someone tries to page `<player>` and cannot, either because `<player>` is set HAVEN or because of `<player>`'s page lock, they are shown `<message>`, if it evaluates to something non-null.

### Example
```sharp
> @set me=HAVEN
> @haven me=I'm AFK and can't answer pages. Please @mail instead.
```


::: seealso
- [HAVEN]
- [page]
- [LOCKING]
- [@away]
- [@idle]
:::
# @hide
`@hide[/<switch>] <descriptor>`<br>
`@hide[/<switch>] [<player>]`

This command allows players to hide their online status. Hidden connections don't show up on WHO, in lwho(), etc, when used by players without see_all.

The first form of this command affects the single connection specified by *\<descriptor\>* (as returned by ports(), or shown on the admin WHO). The second affects all connections for the given *\<player\>*, or the enactor if no *\<player\>* is given. You must be a Wizard, Royalty, or have the Hide @power to affect your own connections; only Wizards can affect other players' connections.

The `/on` and `/yes` switches hide connections, while `/off` and `/no` unhide connections. If no switch is given, the command acts as a toggle: for a single descriptor, the hide status is reversed. For a player, if all their connections are hidden, they will be unhidden. If any are unhidden, they will all be hidden.


::: seealso
- [HIDDEN()]
- [who]
- [LWHO()]
- [LPORTS()]
- [PORTS()]
:::
# @idescribe
# @oidescribe
# @aidescribe
`@idescribe <object>[=<description>]`<br>
`@oidescribe <object>[=<message>]`<br>
`@aidescribe <object>[=<action list>]`

@idescribe command sets the internal description for an object, which is shown to anyone who enters or looks while inside the object. It's only used for players and things; rooms and exits always use @describe.

The @oidescribe attribute is shown to others inside `<object>` when someone looks at the @idescribe, and the @aidescribe is triggered when someone lookst at the @idescribe.

If there is no IDESCRIBE set for an object, those who enter or look inside it will see its @describe. In this case, others in the object will see nothing, and the @aidescribe will not be triggered. If you want to use @aidescribe without @idescribe, set @idescribe to a blank string, or to u(describe) to show the description.


::: seealso
- [enter]
- [@aenter]
- [ENTER_OK]
- [@describe]
- [look]
- [@idescformat]
- [verbs]
:::
# HUH_COMMAND
This internal command is run whenever someone attempts to run a command which doesn't match any built-in or softcoded commands. The huh_command command cannot be run directly, but it can be @hook'd to perform custom actions when an invalid command is entered.

### Examples
```sharp
> &cmd.huh #0=$huh_command: @pemit/sil %#=Whu?
> @hook/override huh_command=#0, cmd.huh
> dsfsdf
Whu?
```

```sharp
> &cmd.huh #0=$huh_command *: @pemit/sil %#=Whu? What is '%0'?
> sdfsdf ert
Whu? What is 'sdfsdf ert'?
```

```sharp
> &cmd.huh #0=$huh_command *: &typos %#=add(default(%#/typos,0),1) ; @pemit/sil %#=Huh? %b(Type "help" for help.) ; @break mod(get(%#/typos),10) ; @wall %n wins %p [ordinal(div(get(%#/typos),10))] typo trophy!
> asfdsf (10 times)
Huh? (Type "help" for help.) (10 times)
Announcement: Room Zero shouts, "Dunce wins his first typo trophy!"
```


::: seealso
- [@hook]
- [evaluation order]
- [WARN_ON_MISSING]
- [UNIMPLEMENTED_COMMAND]
:::
# @idle
`@idle <player>[=<message>]`

This message is sent in return to every page which reaches you, if it evaluates non-null. It is useful if you are idle for long periods of time and wish to tell people where you are, or if you are in a meeting and cannot quickly return pages.

### Example
```sharp
> @idle me=switch(idle(me),>120,I'm idle. Use @mail)
```

Players paging me will only see the "I'm idle" message if I've been idle for over 2 minutes (120 seconds).


::: seealso
- [@away]
- [@haven]
:::
# @infilter
`@infilter <object>[=<pattern 1>[, <pattern 2>[, ..., <pattern N>]]]`

@infilter is meant to be used on objects that have an @listen of "*" (ie, objects that listen to everything, which is commonly used for things like chairs so that someone inside the object can hear everything said/done outside it). @infilter filters out any messages that match one of the patterns and prevents those inside the object from hearing them. It does not stop the @ahear of the listening object from being triggered by things that match the @listen.

Sounds are only forwarded if the speaker also passes `<object>`'s @lock/infilter, which receives the sound heard as %0.

For an explanation of infilter patterns, see the help for "@filter".


::: seealso
- [@filter]
- [@listen]
- [@inprefix]
- [AUDIBLE]
- [listening]
:::
# @inprefix
`@inprefix <object>[=<message>]`

When an object has an @listen, any string it hears which is propagated to its contents will be prefixed with `<message>`. Useful for vehicles, etc, which have an @listen of "*".

### Example
```sharp
> @create Vehicle
Created: Object #103.
> @create Test
Created: Object #104.
> @inprefix Vehicle=From outside,
> @listen Vehicle=*
> enter Vehicle
> @force #104=:bounces.
From outside, Test bounces.
```


::: seealso
- [@prefix]
- [@listen]
- [@infilter]
:::
# @kick
`@kick <player>`

Disconnects every connection `<player>` has, telling each one "You have been disconnected." It needs the `server.operate` permission. See [@boot] to disconnect a single connection.


::: seealso
- [@ps]
- [queue]
:::
# @lemit
`@lemit[/<switch>] <message>`

Emits a message to the outermost container object. For example, if you are carrying a bird, and are inside a vehicle which is in room #10, and you force the bird to @lemit "Cheep", everyone in room #10 will hear "Cheep". This command is the same as "@emit/room".

With the `/silent` switch, no confirmation message is shown. With `/noisy`, it is. If neither is given, the silent_pemit option determines if it is shown.<br>
The `/noeval` switch prevents `<message>` from being evaluated.<br>
The `/spoof` switch causes nospoof notifications to show the enactor's dbref instead of the executor's dbref, and requires control over the enactor or the Can_spoof power.


::: seealso
- [@remit]
- [@nspemit]
:::
# @link
`@link[/preserve] <object>=[<dbref> | here | home | variable]`

Links `<object>` to either a room or a thing. If a thing or player is linked, that sets the object's HOME. If a room is linked, that sets the object's drop-to room, which is where objects that are dropped in the room will be sent to.

Most often, @link is used to link or relink an exit to a destination room. In order to link an exit to a room, you must either own or control the room, OR the room must be set LINK_OK. If the exit is currently unlinked, and you pass its @lock/link, you may link it even if you do not own it. In this case, the exit will be @chowned to you (and set HALT). The Wizard-only `/preserve` switch can be used to link without @chowning and HALTing the exit.

If the destination of an exit is "variable", its destination is determined at the time of travel by the attribute DESTINATION on the exit, which is evaluated like a U()-function. You must have permission to link to whatever the DESTINATION evaluates to in order for the exit to work. If there's no DESTINATION attribute, the EXITTO attribute is also tried.

If the destination is "home", those who travel through the exit will be sent to their homes.

LINK_OK objects can also be used as semaphores, and any object can be @parented to them.


::: seealso
- [EXITS]
- [@open]
- [@dig]
- [DROP-TOS]
- [HOMES]
:::
# @destination
# @exitto
# Variable Exits
`@destination <exit>[=<destination>]`<br>
`@exitto <exit>[=<destination>]`

The DESTINATION attribute is used by variable exits. To make a variable exit, you create it in the usual way (with @open), then @link it to "variable" instead of a dbref. When someone attempts to pass through the exit, the DESTINATION attribute will be evaluated, and should return a dbref; the dbref will be used as the location for the person to go to. The exit name or alias the moving player used is passed to the attribute as %0.

The exit must be able to @link itself to the dbref returned by the attribute. This means the exit must control the destination, the destination must be set LINK_OK, or the exit must have the Link_Anywhere @power.

If no DESTINATION attribute is set on a variable exit, the MUSH will also check for an EXITTO attribute, for cross-platform compatability. It works exactly the same as the DESTINATION attribute.

Note that, unlike most attributes, @destination cannot be abbreviated and must be typed in full.

### Example
```sharp
> @open Random Exit;re
> @link re=variable
> @power re=link_anywhere
> @destination re=pickrand(#5 #123 #999 [home(%#)] %L)
```


::: seealso
- [EXITS]
- [@link]
- [@open]
- [LINK_OK]
- [@power]
:::
# @lset
`@lset <object>/<lock type>=[!]<flag>`

This commands sets or clears flags on locks.<br>
Valid flags include:

- visual (v) This lock can be seen even if the object it's on isn't visual.
- no_inherit (i) This lock isn't inherited off of parents. All locks are set no_inherit by default.
- no_clone (c) This lock isn't copied by @clone.
- wizard (w) This lock can only be set by wizards.
- locked (+) This lock can only be set by the owner of the lock.


::: seealso
- [LOCKING]
- [LOCKFLAGS()]
- [LLOCKFLAGS()]
- [LSET()]
:::
# @log
`@log[/<switch>] <message>`<br>
`@log/recall/<switch> [<number>]`

This command writes `<message>` to the server log, tagged with the object executing the command. It needs the `server.operate` permission. The available switches are /check, /cmd, /conn, /err, /trace, and /wiz, specifying which log category to use. /cmd is default.

Adding the `/recall` switch displays the last `<number>` entries in that category: 100 if omitted, and at most 1000.


::: seealso
- [@logwipe]
:::
# @logwipe
`@logwipe/<log>[/<switch>] <password>`

In PennMUSH this God-only command erases one of the game's log files: `<log>` is one of /check, /cmd, /conn, /err (default), /trace or /wiz, and the policy is /rotate, /trim or /wipe (default).

SharpMUSH owns no log files. Its logs go to the logging sinks named in its configuration (the console, by default), and rotating, trimming or clearing them is done there. @logwipe therefore performs no operation: it tells God which policy on which log cannot be carried out, returns `#-1 NOT SUPPORTED`, and records the attempt in the server log.


::: seealso
- [@log]
:::
# @moniker
`@moniker <object>[=<moniker>]`

This command sets or clears the "moniker" for `<object>`. A moniker is an ansi template, to show the object's name in color. Exactly where this color is displayed depends on the "monikers" @config option; see [monikers] for more information.

`<moniker>` can contain any text - it will be ignored, and only the ansi colors will be taken into account. If `<object>`'s name is longer than `<moniker>`, the last color will be used for the remaining letters.

### Examples
Display your name in highligted red
```sharp
> @moniker me=ansi(hr,-)
```

Show the first letter in orange, and the rest with no color
```sharp
> @moniker me=ansi(+orange,-)[ansi(n,-)]
```


::: seealso
- [monikers]
- [MONIKER()]
- [ansi()]
- [@nameformat]
- [@nameaccent]
- [MONIKER()]
:::
# @name
`@name <object>=<new name>`<br>
`@name <player|exit>=<new name>[;<alias1>[;<aliasN>]]`

Changes the name of `<object>` to `<new name>`.

Players can change their name to anything valid which is not currently in use by another player, as a name or alias. (They can change their name to something from their own @alias.)

You can change the alias for a player or exit while renaming it, by giving the alias(es) after the new name, each separated by a semicolon. If the name is followed by a semicolon with no aliases, the existing alias will be cleared instead.

When `<object>`'s name is changed, its ONAME and ANAME verb attributes will be triggered. See [@ONAME] for details.

### Examples
```sharp
> @name here=My Room
Name set.
> @name me=Mike;Michael;m
Alias set.
Name set.
> @name me=Obi Wan;
Alias removed.
Name set.
```


::: seealso
- [@alias]
- [@ONAME]
- [NAME()]
- [FULLNAME()]
:::

Config options: player_name_spaces, player_name_len, only_ascii_in_names
# @oname
# @aname
`@oname <object>[=<message>]`<br>
`@aname <object>[=<action list>]`

Whenever `<object>`'s name is changed (via @name), others in the same location will see the contents of `<object>`'s ONAME attribute, prepended with `<object>`'s new name. At the same time, `<object>`'s ANAME attribute will be triggered. Both attributes receive the old name as %0, and the new name as %1.

### Example
```sharp
> @oname me=has regenerated from %0!
> @aname me=think >> Renamed from %0 to %1 at [time()] by %n(%#).
```


::: seealso
- [@name]
- [NAME()]
- [verbs]
:::
# @newpassword
`@newpassword <player>=<password>`<br>
`@newpassword/generate <player>`

This wizard-only command changes `<player>`'s password. If `<player>` is connected, she will be informed that the password was changed and who by, but not what it was changed to.

The `<player>` argument is evaluated, but the `<password>` argument is not when the command is entered directly from a client.

If the `/generate` switch is given, a new, random password is generated automatically, and shown to the enactor (but not to `<player>`).<br>
The `<password>` must not contain whitespace, unprintable characters, or '='.


::: seealso
- [@password]
- [CHECKPASS()]
:::
# @nspemit
# @nsemit
# @nslemit
# @nsremit
# @nszemit
# @nsoemit
# @nsprompt
`@nsemit[/<switch>] [<message>]`<br>
`@nslemit[/<switch>] <message>`<br>
`@nspemit[/switches] <object>=<message>`<br>
`@nsprompt[/switches] <object>=<message>`<br>
`@nsremit[/switches] <object>=<message>`<br>
`@nsoemit[/<switch>] [<room>/]<object> [<object>...]=<message>`<br>
`@nszemit <zone>=<message>`

These commands work like @emit, @lemit, @pemit, @prompt, @remit, @oemit, and @zemit, respectively, but will not include nospoof information if used by Wizards or someone with the Can_spoof @power. They are meant to be used by commands in the master room where the nospoof information is just useless noise. They take the same switches as their respective commands, with a few exceptions (`/spoof`, and for @nspemit, `/contents`). SharpMUSH also supports the privileged `/port` and `/port/list` forms on @nspemit.


::: seealso
- [@emit]
- [@lemit]
- [@pemit]
- [@prompt]
- [@remit]
- [@oemit]
- [@zemit]
- [EMIT()]
- [NSLEMIT()]
- [PEMIT()]
- [PEMIT()]
- [REMIT()]
- [OEMIT()]
- [ZEMIT()]
- [PROMPT_NEWLINES]
:::
# @open
`@open <exit name>[=<destination>,<return exit name>,<source room>,<dbref>,<return dbref>]`

This command opens an exit, named `<exit name>`, in your current location, or in `<source room>` if one is given. Exits can only be opened from rooms. If a `<destination>` is given, the exit will be linked (as per @link) to that object. If you don't have permission to link to `<destination>`, the exit will be created but unlinked.

Output: the new exit's dbref, as `open()` returns it.

If `<return exit name>` is given, the MUSH will attempt to open an exit back from `<destination>` and link it to `<exit name>`'s source.

Both `<exit name>` and `<return exit name>` can include any number of aliases for the exits, separated by semicolons. See [@name] for details.

Wizards and objects with the PICK_DBREFS power can specify garbage dbrefs to use for the exit and return exit.

To open an exit in a room, you must control the room, have the Open_Anywhere @power, or the room must be set OPEN_OK and you must pass its @lock/open.

### Example
```sharp
> @open Up \<U\>;up;u;climb=#255, Down \<D\>;down;d;fall
```


::: seealso
- [EXITS]
- [@link]
- [@dig]
- [OPEN()]
:::
# @parent
`@parent <object>[=<parent>]`

This command sets the parent of `<object>` to `<parent>`. If no `<parent>` is given, or `<parent>` is "none", `<object>`'s parent is cleared. You must control `<object>`, and must either control `<parent>` or it must be set LINK_OK and you must pass its @lock/parent.


::: seealso
- [parent]
- [PARENT()]
- [LPARENT()]
- [ANCESTORS]
:::
# @package
`@package/scan <objects>`<br>
`@package <objects>=<package-id>[,<version>[,<description>[,<title>]]]`

Wizard-only. Turns one or more live objects into a softcode package manifest (`package.yaml`), the same format the web authoring panel produces at `/admin/packages/author`. `<objects>` is a space-separated list of dbrefs or names.

`@package/scan` is read-only: it lists each object, the manifest ref it would be given, and any dbrefs the objects reference *outside* the selection.

`@package <objects>=<package-id>` exports the selection and pemits the resulting manifest back to you. Dbrefs that point at another selected object are converted to symbolic `{{ref}}` tokens automatically. `<version>` defaults to `1.0.0`, `<description>` defaults to an auto-generated note, and `<title>`, the name the package is listed under, defaults to the first object's name.

This single-step export only succeeds when the selection is **self-contained**: every dbref in the objects' attributes points at another selected object. If any attribute references an object outside the selection, that dbref must be classified as a well-known object or a configure parameter, which is done in the web authoring panel; `@package` will tell you which dbrefs are unresolved and point you there.

`@package` obeys the same visibility rules as `@decompile`: an object must pass your examine permission, and only the attributes you can see (VEILED attributes excluded) are scanned or written into the manifest.


::: seealso
- [@decompile]
:::
# @password
`@password <old password>=<new password>`

This changes your password. Please note that passwords ARE case-sensitive. The arguments are not evaluated.<br>
The `<new password>` must not contain whitespace, unprintable characters, or '='.


::: seealso
- [@newpassword]
- [CHECKPASS()]
:::
# @receive
# @oreceive
# @areceive
`@receive <recipient>[=<message>]`<br>
`@oreceive <recipient>[=<message>]`<br>
`@areceive <recipient>[=<action list>]`

These attributes contain the message shown `<recipient>` when he receives an object (via 'get' or 'give'), the message shown to others in `<recipient>`'s location when he receives an object, and the actions to be taken by `<recipient>` when he receives an object, respectively.

In all cases, %0 is the dbref of the object received. If the object was 'give'n, %1 will be the dbref of the giver.


::: seealso
- [give]
- [get]
- [@give]
- [@asuccess]
- [action lists]
- [verbs]
:::
# @give
# @ogive
# @agive
`@give <giver>[=<message>]`<br>
`@ogive <giver>[=<message>]`<br>
`@agive <giver>[=<action list>]`

These attributes contain the message shown to `<giver>` when he gives an object, the message shown to others in `<giver>`'s location when he gives an object, and the actions to be taken by `<giver>` when he gives an object, respectively.

In all cases, %0 is the dbref of the object being given, and %1 is the dbref of the recipient.


::: seealso
- [give]
- [@receive]
- [action lists]
- [verbs]
:::
# @pcreate
`@pcreate <name>=<password>[, <dbref>]`

This wizard-only command creates a player with the given name and password. If specified, `<dbref>` is the dbref of a garbage object to be used for the new player.

Output: the new player's dbref, as `pcreate()` returns it.


::: seealso
- [PCREATE()]
:::
# @prompt
`@prompt[/<switch>] <dbref list>[=<message>]`

A variation of `@pemit/list` that adds a telnet GOAHEAD control code to the end of messages sent to players. Players with clients that handle GOAHEAD may get the message as a prompt in their client's input area.

If `<message>` is omitted, an empty prompt is sent.

@prompt supports the following @pemit switches: `/silent`, `/noisy`, `/spoof`, `/noeval`


::: seealso
- [@pemit]
- [@nspemit]
- [PEMIT()]
- [PEMIT()]
- [PROMPT_NEWLINES]
:::
# PROMPT_NEWLINES
`PROMPT_NEWLINES [1|0]`

This socket-level command is used to indicate whether a newline should be sent after the telnet GOAHEAD code issued by @prompt/prompt() to telnet-capable clients. By default, in order to maximize portability, newlines are sent.

Some clients, like TinyFugue, are smart enough to interpret GOAHEAD and treat prompts specially by putting them into their input window. These clients do not require the newline, and sending the newline results in a blank line in their output window. The 'PROMPT_NEWLINES 0' command can be used to disable the newline and is recommended for users with these clients.


::: seealso
- [@prompt]
- [PEMIT()]
- [TERMINFO()]
- [@SOCKSET]
:::
# @poll
`@poll`<br>
`@poll <message>`<br>
`@poll/clear`

This command manipulate the message at the top of WHO/DOING. By itself, it displays the current poll. Wizards and those with the poll @power can set or clear the message.


::: seealso
- [@doing]
- [who]
- [who]
:::
# @poor
`@poor <player>`

This command sets `<player>`'s building quota to 0, so they cannot build until it is raised again with [@quota]. It needs the `players.moderate` permission, and refuses when quotas are disabled.


::: seealso
- [MONEY]
- [give]
:::
# @prefix
`@prefix <object>[=<message>]`

This attribute is meant to be used with the AUDIBLE flag. It is evaluated, with %0 the message, and prepended to the sound an AUDIBLE object passes on to its @forwardlist; without one, the sound starts "From `<object>`, ". SharpMUSH does not propagate sound through AUDIBLE exits yet, so @prefix is not used for them.


::: seealso
- [@inprefix]
- [AUDIBLE]
- [@listen]
:::
# @purge
@purge is a wizard only command that calls the internal purge routine to advance the clock of each object scheduled to be destroyed, and destroy those things whose time is up. The internal purge routine is normally run automatically approximately every 10 minutes.

The @purge command should almost never need to be performed manually. If you do use it manually, you may want to use it twice in a row to make sure that everything marked GOING is actually destroyed.


::: seealso
- [@dbck]
:::
# @readcache
`@readcache`

This command rereads the text files and rebuilds the indexes for help, news and similar commands. Changes to those files while the game is running are not seen until @readcache is used. It needs the `server.operate` permission.

@readcache does not reload configuration; game options are changed with [@config].


::: seealso
- [@shutdown]
:::
# @remit
`@remit[/switches] <object>=<message>`

Sends the message to all contents of `<object>`, which can be a room, thing, or player. The message is also sent to the `<object>` itself. (The TinyMUSH equivalent is `@pemit/contents`).

The `/silent` switch stops the remitter from getting feedback if they're in a different location than the target.<br>
The `/noisy` switch always gives feedback to the remitter if they are not in the target location.<br>
(Without `/silent` or `/noisy`, the silent_pemit config option is used to determine noisiness.)<br>
The `/list` switch will send the message to the contents of multiple objects at the same time. The `<object>` argument is treated as a space-separated list of targets.<br>
The `/spoof` switch causes nospoof notifications to show the enactor's dbref instead of the executor's dbref, and requires control over the enactor or the Can_spoof power.<br>
The `/noeval` switch causes `<message>` to not be evaluated.


::: seealso
- [@emit]
- [@pemit]
- [@oemit]
- [SPOOFING]
- [NOSPOOF]
- [CONTROL]
:::
# @restart
`@restart <object>`<br>
`@restart/all`

This command halts `<object>` (as described in @halt), and then triggers the STARTUP attribute on the object, if set. If `<object>` is a player, it affects the player and all of their objects. Players can use `@restart me` to restart their own objects. The `/all` switch halts all objects (see @allhalt) and restarts them, and can only be used by a wizard.


::: seealso
- [@halt]
- [@startup]
- [@shutdown]
:::
# @scan
`@scan[/<switches>] <command>`

@scan gives you a list of all objects containing $-commands (user-defined commands) which could match `<command>`. If given no switches, it checks you, your possessions, your location, objects in your location, the zone/zone master room of your location, your zone, and objects in the master room. It does NOT stop when it gets a match, but rather, finds all possible matches. It also tells how many commands on each object were matched, and what attributes they are in. It does NOT scan objects that you do not control and are not set VISUAL.

Output: the `<object>/<attribute>` pairs matched, as `scan()` returns them.

This command any combination of these four switches:
- /room -- just matches on your location and objects in it.
- /self -- just matches on you and anything you're carrying.
- /zone -- just matches on zones of your location and yourself.
- /globals -- just matches on objects in the master room.

If no switch is given, all locations are checked. `<command>` must be entered exactly as you would type it (so, to match the $-command `$foo *:` you must type '`@scan foo <something>`', not just '`@scan foo`').


::: seealso
- [$-commands]
- [evaluation order]
:::
# &
`&<attribute> <object>[=<value>]`

Sets `<attribute>` on `<object>` to `<value>`, and clears it when `<value>` is omitted. It is the short form of `@set <object>=<attribute>:<value>`, and the form almost all softcode uses.

The attribute name is evaluated before the attribute is set, so `&hdr_%q1 me=...` stores into whatever `%q1` holds. The value is NOT evaluated: what you type is what is stored, and braces around it are kept, so `&cmd me={think [add(1,2)]}` stores the code rather than the number 3.


::: seealso
- [@set]
- [attributes]
- [~]
:::
# ~
`~<command>`

Runs `<command>` with strict argument parsing. SharpMUSH normally splits a command's arguments with error recovery: a syntax error in the argument text is patched up and the split returns its best effort. Under `~`, the recovery is switched off and that error answers `#-1 PARSER FAILURE` instead.

This is narrower than it sounds, and it is **not** what makes a malformed expression an error; that happens anyway. `think [add(1,2)` answers `#-1 PARSER FAILURE` with or without `~`, because an argument whose split reported errors is re-parsed strictly before it is evaluated. What `~` changes is the split itself, so a command whose argument structure only survived by error recovery fails instead of running on a best-effort reading of what you typed.

Nesting is limited by the `max_depth` configuration option, as it is for [@@] and the other command modifiers.


::: seealso
- [&]
- [@@]
- [restrictedexpr]
:::
# ATTRIB_SET
# @_
`&<attr> <object>[=<value>]`<br>
`@_<attr> <object>[=<value>]`<br>
`ATTRIB_SET/<attr> <object>=<value>`

The `&<attr>` and `@_<attr>` commands can be used to set or clear an attribute from an object. When entered directly from a client, they do not evaluate the `<value>`.

ATTRIB_SET is the internal command which powers &attr and @_attr setting; it cannot be used directly, but can be restricted or @hook'd to change the behaviour of &attr/@_attr-setting.


::: seealso
- [@set]
- [ATTRIB_SET()]
:::
# @gender
`@gender <object>[=<gender>]`

Sets the gender of `<object>` in the attribute the `gender_attr` option names, which is the attribute pronoun substitution reads. That is SEX unless the game has changed it, so on most games `@gender` and `@sex` do the same thing. With no `=`, the attribute is cleared.

`@gender` works exactly like `@sex` and the other `@<attribute>` commands: you must be able to set the attribute on `<object>`, the value is stored as typed, and you are told it was set unless you are QUIET. If `gender_attr` is changed, `@gender` sets the new attribute from then on. `@sex` always sets SEX. `@gender` is a SharpMUSH addition.

```sharp
> @gender me=Female
```
Output: "Yourname/SEX - Set." on a game that keeps gender in SEX, or the name of the attribute `gender_attr` names in its place.

```sharp
> @gender me
```
Output: "Yourname/SEX - Cleared." on a game that keeps gender in SEX.


::: seealso
- [@sex]
- [@config attribs]
- [GENDER]
- [SUBJ()]
:::
# @sex
`@sex <player>[=<gender>]`

You can use this command to set yourself or any of your objects to be male, female, neuter, or plural. The SEX attribute is used for pronoun substitution by the MUSH, and anything not recognizable will be treated as neuter.

### Examples
```sharp
> @sex me=Male
> @sex me=Female
> @sex me=Woman
> @sex me=They
> @sex me=Plural
> @sex me=No thank you (silly, but possible)
```

`@sex` always sets the SEX attribute, as it does in PennMUSH. If the game keeps gender in another attribute (the `gender_attr` option), use [@gender] instead.


::: seealso
- [@gender]
- [@config attribs]
- [GENDER]
- [SUBJ()]
- [POSS()]
- [APOSS()]
- [OBJ()]
:::
# @shutdown
`@shutdown[/panic][/reboot][/paranoid]`

@shutdown tells everyone connected that the game is shutting down and logs it, but does not stop the game: the server's process is stopped by whatever runs it, such as Docker or the host. The world is saved as it changes, so there is nothing to save first. It needs the `server.operate` permission.

`@shutdown/panic` does the same, logged as a panic. It may only be used by God.

`@shutdown/reboot` restarts the game engine without disconnecting anyone: connections are held by the connection server while the engine stops and starts again. Everyone connected is told first. The engine is started again by whatever runs it, such as Docker's restart policy; run by hand, it just stops. A restart is what starts or stops a plugin after [@plugin] changes it. It needs the `server.operate` permission.

`@shutdown/paranoid` announces that the database is being saved and logs the request; it writes nothing, because the world is always on disk.
# @slave
`@slave/restart [info|ssl]`

@slave is accepted for compatibility and does nothing: SharpMUSH has no helper daemons to restart, and it only replies that the command does nothing. It needs the `server.operate` permission.
# colorstyle
`SOCKSET colorstyle=<value>`<br>
`@SOCKSET [me|<descriptor>]=colorstyle,<value>`

You can override the color format you receive from SharpMUSH. Normally, SharpMUSH tries to guess what your client is capable of through telnet negotiation and your player flags. @sockset lets you inform SharpMUSH that your client can support more colors than expected.

Colorstyle options are:

- plain: Plain text. No markup whatsoever.
- hilite: You only receive hilite text. No colors, just ansi-hilite.
- 16color: You receive hilite text and the ANSI 16 colors.
- xterm256: You receive xterm-style 256 colors for text and background.
- truecolor: You receive 24-bit RGB colors for text and background. Also accepted as 'rgb' or '24bit'.
- auto: go back to what SharpMUSH determined was your client's capabilities.

SharpMUSH determines 'auto' from the terminal type your client reports (RFC 1091), including the MTTS capability bits when your client sends them. terminfo() shows the style in effect.

In the event that your client receives a color that it is unable to display, SharpMUSH will attempt to find a close match that can fit your client's capabilities.


::: seealso
- [ANSI]
- [COLOR]
- [XTERM256]
- [@SOCKSET]
:::
# hyperlinks
# commandlinks
`SOCKSET hyperlinks=<on|off|auto>`<br>
`SOCKSET commandlinks=<on|off|auto>`

Some terminals can make links clickable. With hyperlinks on, a web link is sent as an OSC 8 hyperlink, which opens in your browser when clicked. With commandlinks on, a command link (one that runs a command, such as the topics at the end of a help file) is sent as an MSLP link, which your client sends back to the game as a command when clicked.

Neither is sent unless SharpMUSH knows your client reads it, because a client that does not may print the codes as text. 'auto' (the default) decides from your client:

- hyperlinks: on when your terminal is one known to draw them (see [TERMINAL]), or your client is Mudlet.
- commandlinks: on when your client reports MSLP in its MTTS capabilities.

Use 'on' or 'off' to override that, and 'auto' to go back to it. SOCKSET shows the setting in effect, as "auto (on)" or "auto (off)" when nothing is set. terminfo() includes 'hyperlinks' and 'commandlinks' while they are on.

Example:
```
> SOCKSET hyperlinks=on
Hyperlinks set to 'on'
```


::: seealso
- [@SOCKSET]
- [GRAPHICS]
- [TERMINFO()]
:::
# animation
`SOCKSET animation=<on|off>`<br>
`@SOCKSET [me|<descriptor>]=animation,<on|off>`

With animation on, a moving picture (an animated GIF) plays, as long as pictures are on (see [GRAPHICS]) and drawn in a way that can move: kitty sends every frame, and iterm2 sends the picture as an animated GIF. With it off (the default), or drawn as sixel or blocks, a moving picture shows its first frame.

A terminal known not to play moving pictures (Ghostty, Konsole, mintty and Visual Studio Code among them) is sent the first frame even with animation on. terminfo() includes 'animation' while moving pictures play.

Example:
```
> SOCKSET animation=on
Animation set to 'on'
```


::: seealso
- [GRAPHICS]
- [TERMINAL]
- [@SOCKSET]
:::
# terminal
`SOCKSET terminal=<name|auto>`<br>
`@SOCKSET [me|<descriptor>]=terminal,<name|auto>`

SharpMUSH works out which terminal you use from the terminal type your client reports, or from its answer when you use 'SOCKSET graphics=detect'. That decides which links are sent (see [HYPERLINKS]) and, once pictures are on, how they are drawn (see [GRAPHICS]). Most clients report a generic type such as xterm-256color, which names no terminal.

Naming your terminal here tells SharpMUSH what it is, for one that does not say (Windows Terminal never does). 'auto' goes back to what your client reported. SOCKSET shows the terminal in use, as "auto (<terminal>)" when you have not named one.

The terminals SharpMUSH knows:

- kitty, ghostty, wezterm, iterm2, konsole, foot, xterm, windows-terminal, mintty, vscode (Visual Studio Code's terminal), contour, rio, mlterm, alacritty, vte (GNOME Terminal, Tilix, Terminator and other VTE terminals), tmux

Example:
```
> SOCKSET terminal=windows-terminal
Terminal set to 'windows-terminal'
```


::: seealso
- [GRAPHICS]
- [ANIMATION]
- [HYPERLINKS]
- [@SOCKSET]
:::
# @speechmod
`@speechmod <object>[=<modifier>]`

When set, this attribute modifies everything `<object>` says, poses, semiposes and @emits. The original text spoken/posed/emitted is passed as %0 or %`\<message\>`, with %1 or %`\<type\>` passed as " (for say), : (for pose), ; (for semipose) or | (for @emit). Each is passed by position and by name (see [named arguments]).

If the attribute evaluates to an empty string, the original text will be used. Otherwise, the result of the attribute is used.

### Example
```sharp
> @speechmod me=ucstr(%<message>)!
> say hello
You say, "HELLO!"
> pose waves
Bob WAVES!
```

```sharp
> @speechmod me=switch(%1,",ucstr(%0),:,lcstr(%0))
> say Test
You say, "TEST"
> pose Test
Bob test
> @emit Test
Test
```


::: seealso
- ["]
- [:]
- [@emit]
- [@chatformat]
- [@pageformat]
:::
# @startup
`@startup <object>[=<action list>]`

Sets the list of actions on `<object>` that will happen whenever the MUSH is restarted. This lets you start up objects that need to be running continuously. It is also useful for setting up @functions and @hooks, which are not saved across restarts.

@startup is also triggered when an object is @restarted or @undestroyed.

Note that @startups are NEVER inherited from parent objects.


::: seealso
- [@restart]
- [@undestroy]
- [action lists]
- [@function]
- [@command]
- [@hook]
:::
# @stats
`@stats [<player>]`<br>
`@stats/tables`<br>
`@stats/flags`<br>
`@stats/chunks`<br>
`@stats/regions`<br>
`@stats/paging`<br>
`@stats/freespace`

In its first form, display the number of objects in the game broken down by object types: `<total> objects = <rooms> rooms, <exits> exits, <things> things, <players> players.` With a `<player>`, only the objects that player owns are counted. Anyone may count their own objects (`@stats me`) or the whole game's; counting another player's objects needs the Search power or wizard/royalty privileges. The whole-game count ends with a garbage count, which is always 0: a destroyed object is removed rather than kept as garbage.

`@stats/tables` lists SharpMUSH's lookup tables (built-in functions, @functions, commands, flags, powers, attribute definitions, config options and connections) with the number of entries in each.<br>
`@stats/flags` reports, for the FLAG and POWER flagspaces, how many definitions each has and how objects' sets of flags are distributed.

`@stats/chunks`, `/regions`, `/paging` and `/freespace` report PennMUSH's attribute-chunk allocator. SharpMUSH keeps attributes in its database provider and has no chunk allocator, so these switches say so and return `#-1 NOT SUPPORTED`. For how much disk the database itself uses, see `@storage`.
# @sweep
`@sweep[/connected][/here][/inventory][/exits]`

@sweep gives you a list of the objects that are listening in your location, including the room you are in. Most objects only listen for a particular string or phrase, so they normally do not pose a problem if you need privacy. You will have to be careful of players and puppets since they will hear everything you say and do. (And might post the same to r.g.m!)

The options are switches; an argument is ignored. With `/connected`, only connected players and puppets owned by connected players are shown. `/here` checks only your location, `/inventory` only the objects you carry, and `/exits` only AUDIBLE exits in your room.


::: seealso
- [@scan]
:::
# @ulock
# @uunlock
`@ulock <object>[=<key>]`<br>
`@uunlock <object>`

These commands set the Use lock for `<object>` to `<key>`, or clear the Use lock. They are deprecated, and should be replaced with

`@lock/use <object>[=<key>]`<br>
and<br>
`@lock/use <object>`

The Use lock determines who is allowed to "use" the object or trigger any $-commands or ^-listens on the object.

To only lock who can use $-commands, use `@lock/command`. To only lock who can trigger ^-listens, use `@lock/listen`.

Example: if I want everyone but Bob to be able to use my toy, I would "`@lock/use toy=!*Bob`". If I want only Bob to be able to use it, I would "`@lock/use toy==*Bob`".


::: seealso
- [LOCKING]
- [use]
- [locktypes]
:::
# @unlink
`@unlink <exit>`<br>
`@unlink <room>`

The first form of this command unlinks an exit from its destination room. Unlinked exits may be picked up and dropped elsewhere or relinked by anyone else. (Note that relinking an unlinked exit will @chown it to you if you do not already own it.)

The second form removes the DROP-TO on the room.


::: seealso
- [@link]
- [DROP-TOS]
:::
# @unlock
`@unlock[/<switch>] <object>`

Removes the lock on `<object>`. It can take as many switches as @lock can.


::: seealso
- [LOCKING]
- [locktypes]
:::
# @locale
`@locale`<br>
`@locale <tag>`<br>
`@locale =`

Shows or sets the language the server addresses you in. `<tag>` is a BCP-47 language tag such as `en`, `fr` or `de`; with no argument the command reports your current locale, and with an empty argument it clears the setting back to the server default.

The locale applies to the connection that ran the command and is stored as the `LOCALE` attribute on your character, so it is remembered the next time you connect.

This is a SharpMUSH command; PennMUSH has no @locale.


::: seealso
- [@set]
:::
# @map
`@map[/<switches>] <object>[/<attribute>]=<list>`

Runs an attribute once for each element of `<list>`, as [@dolist] does, but passing the element as `%0` rather than substituting it into the command text. The attribute is named as `<object>/<attribute>`.

Output: with `/inline` or `/inplace`, the output of the last command run; queued, nothing. See [command output].

Switches are the queue-control set shared with [@dolist] and [@include]: `/inline`, `/inplace`, `/localize`, `/clearregs`, `/nobreak`, `/notify` and `/delimit`.

This is a SharpMUSH command; PennMUSH spells the same idea with [@dolist] and [MAP()].


::: seealso
- [@dolist]
- [@include]
- [MAP()]
- [@queue]
:::
# @version
`@version`

Tells the player the name of the MUSH, which version of the code is currently running on the system, when it was compiled, and when the last restart was. It may also include some other information, including the MUSH's website address and the GIT revision, if available.

Output: the version, as `version()` returns it.


::: seealso
- [VERSION()]
- [VERSION()]
:::
# @wall
# @rwall
# @wizwall
`@wall[/emit][/noeval] <message>`<br>
`@rwall[/emit][/noeval] <message>`<br>
`@wizwall[/emit][/noeval] <message>`

@wall sends `<message>` to all connected players. @rwall only sends the message to connected wizards and royalty, and @wizwall is seen only be wizards.

`<message>` can be prefixed with : or ; to pose or semi-pose it, respectively, or the `/emit` switch can be given to emit the message. If `<message>` begins with a " and the chat_strip_quote option is on, the " will be stripped.

The message is prefixed with the value of the wall_prefix, rwall_prefix or wizwall_prefix options, depending on the command used.


::: seealso
- [@wall]
- [@wall]
:::
# @wcheck
`@wcheck <object>`<br>
`@wcheck/all`<br>
`@wcheck/me`

The first form of the command performs warning checks on a specific object. The player must own the object or be see_all. When the owner runs the command, the @warnings of the object are used to determine which warnings to give. If the object has no @warning's set, the @warnings of the owner are used. When a non-owner runs the command, the @warnings of the non-owner are used.

The second form of the command runs @wcheck on every object in the database and informs connected owners of warnings. It is usually automatically run by the MUSH at intervals. Only Wizards may use `@wcheck/all`.

The third runs it on all objects the player owns that aren't set NO_WARN.


::: seealso
- [@warnings]
- [WARNINGS]
:::
# @whereis
`@whereis <player>`

If `<player>` is not set UNFINDABLE, this command will tell you where the player is. It will also inform the player that you attempted to locate their position, and whether you succeeded or not.

Output: the player's location, as `loc()` returns it.

To avoid being found this way, just do: `@set me=UNFINDABLE`

### Example
```sharp
> @whereis Moonchilde
```


::: seealso
- [UNFINDABLE]
- [LOC()]
:::
# @wipe
`@wipe <object>[/<attribute pattern>]`

This command clears attributes from `<object>`, with the exception of attributes changeable only by wizards, and attributes not controlled by the object's owner (i.e. locked attributes owned by someone else). Only God may use @wipe to clear wiz-changeable-only attributes. The SAFE flag protects objects from @wipe.

If no `<pattern>` is given, this gets rid of all the attributes, with exceptions as given above. If `<pattern>` is given, it gets rid of all attributes which match that pattern. Note that the restrictions above still apply.

When wiping an attribute that is the root of an attribute tree, all attributes in that tree will also be removed.
# @zemit
`@zemit[/silent|/noisy] <zone>=<message>`

Emits a message to all rooms in `<zone>`. You must have control `<zone>` in order to use this command.

The `/silent` switch suppresses the confirmation message, and `/noisy` causes it to be shown. With neither switch, the silent_pemit @config option determines whether or not the message is shown. The confirmation message is only shown if you are not in a room which would receive `<message>`.


::: seealso
- [@nspemit]
- [ZEMIT()]
- [ZONE()]
- [ZWHO()]
- [zones]
:::
# ahelp
# anews
`ahelp [<topic>]`<br>
`anews [<topic>]`

These commands, if enabled, show the admin-only help or news files for the MUSH. Only Wizards may use them.
# brief
`brief[/opaque] [<object>]`

This command works like an abbreviated version of "examine", showing information about an object including its name, owner, zone, type, flags and powers, locks, channels, warnings, home and location. Unlike "examine", it does not print out all the attributes on the object. It will include the contents of `<object>`, unless the `/opaque` switch is given.

`<object>` defaults to "here".


::: seealso
- [examine]
:::
# cd
# ch
# cv
`cd <name> <password>`<br>
`ch <name> <password>`<br>
`cv <name> <password>`

Not really MUSH commands, but commands available at the connect screen. Wizards can use 'cd' instead of 'connect'; the new connection will be hidden (as per @hide), and the player will be set DARK. Mortals set HEAR_CONNECT will not hear dark wizards connect.

Wizards, Royalty, and those with the Hide @power can use 'ch' to connect with the new connection hidden (as per @hide).

Connecting using 'cv' causes the Dark flag to be cleared prior to connection messages being broadcast.

None of those commands affect the hidden status of other connections, if you're reconnecting.


::: seealso
- [DARK]
- [@hide]
:::
# OUTPUTPREFIX
# OUTPUTSUFFIX
`OUTPUTPREFIX <string>`<br>
`OUTPUTSUFFIX <string>`

Sets your output prefix or suffix. These strings will be shown before and after the output of any command that you initiate, respectively. They are primarily useful for bots and the like.
# IDLE
`IDLE [<string>]`

This command does nothing. It does not reset a connection's idle time. It is useful for people who are connecting from behind a NAT gateway with a short fixed timeout; if you're in this situation, have your client send the IDLE command every minute or so, and the NAT connection won't time out (but you won't appear, to other players, to be active).

Some routers will only consider a connection alive if text is received, as well as sent. If you give a `<string>` with the IDLE command, that same `<string>` will be sent back to you for this purpose.


::: seealso
- [KEEPALIVE]
- [@idle]
:::
# teach
`teach <command>`<br>
`teach/list <action list>`

The teach command shows its argument (unparsed) to others in your location, and then executes it as a command. If the `/list` switch is given, it will run an `<action list>` of commands in much the same way as @triggering an attribute. Otherwise, it executes a single `<command>`, exactly as if you'd entered `<command>` from your client. Useful for helping newbies and demonstrating commands.

Output: the output of the last command it ran. See [command output].

```sharp
> say To do a pose, use :<action>
You say "To do a pose, use :<action>"
> teach :waves hello.
Javelin types --> :waves hello.
Javelin waves hello.
```

```sharp
> teach "[sort(c b a)]
Javelin types --> "[sort(c b a)]
Javelin says, "a b c"
```

```sharp
> teach/list @switch 1=1, say Third; say First; @break 1; say Second
Javelin types --> @switch 1=1, say Third; say First; @break 1; say Second
You say, "First"
You say, "Third"
```


::: seealso
- [@trigger]
- [@include]
:::
# drop
`drop <object>`

Drops `<object>`, if you are presently carrying it. If the room the object is dropped in has a DROP-TO set, the object may automatically be sent to another location.

In order to drop an object, you must pass it's Drop lock and your location's DropIn lock.


::: seealso
- [empty]
- [get]
- [STICKY]
- [DROP-TOS]
:::
# enter
`enter <object>`

Used to enter a thing or player. You can only enter an object if you own it or if it is set ENTER_OK. You must also pass the enter-lock, if it is set. Entering an object triggers is @enter/@oenter/@oxenter messages and its @aenter actions. If you fail the enter-lock, the object's @efail/@oefail/@aefail messages and actions are triggered.

Output: the dbref of the object you enter.

Insides of objects are best used for vehicles, or storage spaces when you don't have a home. You can describe the interior of an object differently from its exterior by using @idescribe.

See: [@aenter], [@aefail], [@ealias], [leave], [LOCKING], [@idescribe], [interiors]
# examine
`examine[/<switches>] <object>[/<attribute>]`

Displays all available information about `<object>`. `<object>` may be an object, 'me' or 'here'. You must control the object to examine it. If you do not own the object, or it is not visible, you will just see the name of the object's owner. May be abbreviated 'ex `<object>`'. If the attribute parameter is given, you will only see that attribute (good for looking at code). You can also wildcard match on attributes.<br>
The * wildcard matches any number of characters except a backtick (`).<br>
The ? wildcard matches a single character except a backtick (`).<br>
The ** wildcard matches any number of characters, including backticks.<br>
For example, to see all the attributes that began with a 'v' you could do ex `<object>`/v**

Output: the dbref of the object examined.

The `/brief` switch is equivalent to the 'brief' command.<br>
The `/debug` switch is wizard-only and shows raw values for certain fields in an object.<br>
The `/mortal` switch shows an object as if you were a mortal other than the object's owner and is primarily useful to admins. This switch ignores the object's VISUAL flag (but not its attribute flags)<br>
The `/parent` switch show attributes that would be inherited from the object's parents, if you have permission to examine the attributes on the parent.<br>
The `/all` switch shows the values of VEILED attributes.<br>
The `/opaque` switch omits contents listings.


::: seealso
- [attribute trees]
- [brief]
- [LATTR()]
- [WILDCARDS]
:::
# follow
`follow <object>`

You begin following `<object>`, a player or thing, if you pass its follow lock (see [@lock/follow]). As the object moves around (except if it @teleports away or goes home), you will automatically move around with it, so long as you pass all the locks and enter/leave locks on the exits and things the object moves through. This doesn't prevent you from going somewhere else on your own.


::: seealso
- [unfollow]
- [dismiss]
- [desert]
- [FOLLOWERS()]
- [FOLLOWING()]
- [@follow]
- [@follow]
- [@follow]
:::
# dismiss
`dismiss <object>`<br>
`dismiss`

The dismiss command stops `<object>` from following you. If no object is given, it stops everyone from following you.


::: seealso
- [follow]
- [unfollow]
- [desert]
- [FOLLOWERS()]
:::
# desert
`desert <object>`<br>
`desert`

The desert command stops `<object>` from following you and stops you from following `<object>`. That is, it's shorthand for 'unfollow `<object>`' and 'dismiss `<object>`'. If no object is given, it stops everyone from following or leading you.


::: seealso
- [follow]
- [unfollow]
- [dismiss]
- [FOLLOWERS()]
- [FOLLOWING()]
:::
# empty
`empty <object>`

The empty command attempts to move all the contents of `<object>` to `<object>`'s location. You must either be holding `<object>` (in which case the command is like getting `<object>`'s `<item>` for each item) or be in the same location as `<object>` (in which case the command is like getting `<object>`'s `<item>` and dropping it).

The empty command assumes that all `<object>`'s items pass through the hands of the player running the command. Therefore, the same kinds of locks and messages that are applied in a possessive get (and, possibly, a drop) are applied to each item in `<object>`. It is therefore possible to fail to empty an object for many reasons, even when you could do so using "extraphysical" methods (teleporting items, forcing the object to drop them, or forcing the items to leave the object.)


::: seealso
- [get]
- [drop]
:::
# get
# take
`get <object>`<br>
`get <box>'s <object>`

The first form of this command lets you pick up `<object>` from your current location. The second form allows you to take `<object>` from inside `<box>`'s inventory.

In both cases, you must pass `<object>`'s Basic @lock, and the @lock/take of it's location.

To get an object from someone else's inventory, the possessive_get @config option must be true (and, if `<box>` is a disconnected player, so must possessive_get_d). `<box>` must also be set ENTER_OK.

'take' is usually an alias for the 'get' command.


::: seealso
- [LOCKING]
- [ENTER_OK]
- [give]
- [drop]
- [@asuccess]
- [inventory]
:::
# @buy
# @abuy
# @obuy
`@buy <object>[=<message>]`<br>
`@obuy <object>[=<message>]`<br>
`@abuy <object>[=<message>]`

These attributes can be set, but they are never shown or run: SharpMUSH has no money, and the "buy" command is not implemented.


::: seealso
- [buy]
- [@pricelist]
- [MONEY]
- [LOCKING]
- [verbs]
- [@cost]
- [give]
:::
# @pricelist
`@pricelist <object>=<item1>:<price1>[,<price2>][ <item2>:...]`

The PRICELIST attribute can be set, but nothing reads it: SharpMUSH has no money, and the "buy" command is not implemented.


::: seealso
- [buy]
- [@buy]
- [MONEY]
- [@cost]
- [give]
- [LOCKING]
:::
# buy
`buy <item>[ from <vendor>][ for <cost>]`

The buy command is not implemented: SharpMUSH has no money. It reads no PRICELIST, checks no @lock/pay and runs no @buy attributes; it only says that buying needs an economy system.


::: seealso
- [@buy]
- [@pricelist]
- [give]
- [@cost]
:::
# give
`give <recipient>=<object>`

SharpMUSH has no money: `give <recipient>=<number>` only answers "Money transfer will not be implemented.", and the `/silent` switch has no effect.

This command gives an `<object>` from your inventory to `<recipient>`. The recipient must be set ENTER_OK or controlled by you, and you must pass his @lock/from. You must also pass `<object>`'s @lock/give, and `<object>` must pass `<recipient>`'s @lock/receive. When you give an object successfully, your GIVE/OGIVE/AGIVE attributes, `<recipient>`'s RECEIVE/ORECEIVE/ARECEIVE attributes, and `<object>`'s SUCCESS/ASUCCESS/OSUCCESS attributes are all triggered.


::: seealso
- [@payment]
- [@cost]
- [LOCKING]
- [inventory]
- [@receive]
- [@give]
- [buy]
- [@asuccess]
:::
# go
# goto
# move
`go[to] <direction>`<br>
`go[to] home`<br>
`move <direction>`<br>
`move home`

Goes in the specified direction. `<Direction>` can be the name or alias of an exit in your area, the enter alias of an object in your area, or the leave alias of the object you are in. You do not need to use the word 'go' or 'move', in fact -- simply typing the direction will have the same effect.

Output: the dbref of the room you arrive in.

'go home' is a special command that returns you to your home room/object.


::: seealso
- [HOMES]
- [@link]
- [@ealias]
- [@ealias]
- [EXITS]
- [movement]
:::
# INFO
`INFO`

This command returns some information about the MUSH you are on, such as its version number, time of last restart, number of players currently connected, and size of database. It can be issued from the connect screen.


::: seealso
- [MSSP-REQUEST]
:::
# inventory
# i
`inventory`

Lists what you are carrying. Can be abbreviated by just 'i', or 'inv'. If you are not set OPAQUE, others will also be able to see what is in your inventory by looking at you.

Output: the dbrefs of what you carry, separated by spaces.

Note that on some MUSHes it is possible to take things that are in someone else's inventory. To be safe, @lock any objects that you do not want to lose.


::: seealso
- [score]
- [get]
- [drop]
- [OPAQUE]
- [LOCKING]
- [@invformat]
:::
# leave
`leave`

The command leave allows you to exit an object you have enter'ed into. When you leave an object, its @leave/@oleave/@oxleave messages are triggered, and its @aleave actions are triggered.

Output: the dbref of the room you arrive in.

The NO_LEAVE flag may be enabled on some MUSHes. Objects set with this flag cannot be left. @lock/leave may also be enabled on some MUSHes, which allows you to set who can leave the object. If you fail to leave, the object's @lfail/@olfail/@alfail messages/actions will be triggered.


::: seealso
- [enter]
- [@leave]
- [@lfail]
- [@ealias]
- [LOCKING]
- [interiors]
:::
# LOGOUT
`LOGOUT`

LOGOUT is similar to QUIT, but instead of disconnecting you from the game completely, it merely disconnects you from your current character and returns you to the opening welcome screen. This is useful if you want to disconnect and then reconnect to another character. Unlike most commands, it is case-sensitive and must be typed in all caps.
# news
`news [<topic>]`

The news system works just like the help system. Many MUSHes use it to provide standard information on the rules, theme, and customized commands of the particular MUSH. It is highly recommended that you read it regularly.
# "
# say
`say[/noeval] <message>`<br>
`"<message>`

Says `<message>` out loud. The message will be enclosed in double-quotes. A single double-quote is the abbreviation for this common command. If the `/noeval` switch is given, `<message>` will not be evaluated.

If you have a SPEECHMOD attribute set, it will be evaluated with `<message>` passed as %0 or %`\<message\>` and " (a double-quote) passed as %1 or %`\<type\>`. The result is shown instead of `<message>`, as long as it evaluates to a non-empty string.

If `<message>` begins with a double-quote and the chat_strip_quote @config option is on, the leading " will be stripped.


::: seealso
- [:]
- [whisper]
- [@SPEECHMOD]
- [@emit]
- [page]
:::
# score
`score`

The score command is not supported: SharpMUSH does not track money, so it only says so. To find looping objects, see [@ps].


::: seealso
- [LOOPING]
- [@ps]
- [queue]
- [MONEY]
- [TRACK_MONEY]
:::
# think
`think <message>`

You can use this command to send a private message to yourself. Pronoun substitution is performed. This is essentially equivalent to doing a "`@pemit/silent me=<message>`".

Output: the text it shows.

One possible use: `@adesc me=think %n just looked at you.`


::: seealso
- [@pemit]
- [@@]
:::
# connect
`connect <player> [<password>]`<br>
`connect "<player with spaces>" [<password>]`<br>
`connect guest`

Connects you to a character from the login screen. Quote a name that contains spaces. A character with no password takes no password argument, and `connect guest` takes the next free guest character if the game offers them.

See [cd] and [cd] to connect with your `DARK` flag forced on or off.


::: seealso
- [QUIT]
- [login]
- [register]
- [who]
:::
# register
`register <name> [<email>] <password>`

Creates a web-portal account from the login screen and puts your connection into account mode, where [make], [play] and [claim] work. Characters are then linked to the account rather than carrying their own login.

The game may refuse the command from your address; see [@sitelock].

This is a SharpMUSH command; PennMUSH's `register` mails a password for a new character instead.


::: seealso
- [login]
- [make]
- [play]
- [@account]
:::
# login
`login <name-or-email> <password>`

Authenticates to an existing account from the login screen and puts your connection into account mode, where [make], [play] and [claim] work. It does not connect you to a character; use [play] for that.

This is a SharpMUSH command; PennMUSH has no account layer.


::: seealso
- [register]
- [play]
- [make]
- [connect]
:::
# make
`make <character> <password>`

Creates a character, links it to the account you are logged in to, and connects you to it. Only works in account mode, which [login] and [register] put you in. To add a character that already exists, use [claim].

This is a SharpMUSH command; PennMUSH's equivalent is `create`, which makes an unlinked character.


::: seealso
- [login]
- [play]
- [register]
:::
# play
`play <character>`

Connects you to one of the characters linked to the account you are logged in to. Only works in account mode, which [login] and [register] put you in.

This is a SharpMUSH command; PennMUSH has no account layer.


::: seealso
- [login]
- [make]
- [claim]
- [connect]
:::
# claim
`claim <character> <password>`

Links a character you already have to the account you are logged in to: one made at the connect screen with `create`, made by staff with [@pcreate], or brought over from a PennMUSH database. The character's own password proves it is yours; for a character on another account of yours, that account's password moves it over. The last word is the password, so a name with spaces needs no quotes. Then [play] connects to it. Only works in account mode, which [login] and [register] put you in.

A character with no password cannot be claimed; staff can link it for you. Once you are playing, [@account] `/claim` does the same in the game, and the portal does it from Account, Claim an existing character. See [accounts claiming].

This is a SharpMUSH command; PennMUSH has no account layer.


::: seealso
- [accounts]
- [login]
- [play]
- [make]
:::
# version
`version`

Reports the game's name, its address if one is published, and the server version (the same lines [@version] prints). It works from the login screen, before you have connected.

A deliberate divergence: PennMUSH has no bare `version` at the login screen, only `@version` in-game. Crawlers and players arriving from MUX-family servers type it unprefixed, and it publishes nothing that `INFO` does not.


::: seealso
- [@version]
- [connect]
:::
# QUIT
`QUIT`

Log out and leave the game. Must be in all capitals.
# unfollow
`unfollow`<br>
`unfollow <object>`

This command stops you from following an object that you were formerly following. If no object is given, you stop following everyone you were following.


::: seealso
- [follow]
- [dismiss]
- [desert]
- [FOLLOWERS()]
- [@follow]
- [@follow]
- [@follow]
:::
# use
`use <object>`

This command attempts to "use" `<object>`. If you do not pass `<object>`'s @lock/use, the UFAIL/OUFAIL/AUFAIL attributes are triggered.

If you pass the lock, you will see `<object>`'s USE attribute, and others in your location will see `<object>`'s OUSE. Depending on `<object>`'s CHARGES attribute, one of `<object>`'s AUSE or RUNOUT attributes will be triggered - see [@charges] for more information.


::: seealso
- [@ause]
- [@charges]
- [LOCKING]
- [@aufail]
:::
# WARN_ON_MISSING
This internal command is run when someone attempts to run a command which starts with a function, for example:
```sharp
&test me=$test: [emit(test)]
```
By default it sends the owner of the offending object a message, so they can fix the code to use a command instead of a function, and the command line is not run. The command is disabled in a new game; enable it with `@command/enable warn_on_missing` (or the `restrict_command` configuration). It can be @hooked to set custom behaviour.


::: seealso
- [HUH_COMMAND]
- [UNIMPLEMENTED_COMMAND]
:::
# UNIMPLEMENTED_COMMAND
This command shows the message "This command has not been implemented." It can be typed directly and @hooked like any other command.

A command added with @command/add and not @hooked shows the same message, but it does so itself: it does not run UNIMPLEMENTED_COMMAND, so a hook on UNIMPLEMENTED_COMMAND does not change it. To change what an added command does, @hook the added command. This differs from PennMUSH; see [compatibility commands].


::: seealso
- [HUH_COMMAND]
- [WARN_ON_MISSING]
- [@command]
- [@hook]
:::
# whisper
# w
`whisper <player>=<message>`<br>
`whisper/silent <player>=<message>`<br>
`whisper/noisy <player>=<message>`<br>
`whisper/noeval <player>=<message>`<br>
`whisper/list <players>=<message>`

Whispers the message to the named person, if they are nearby. If `<message>` is prefixed with a ':' or ';' it will be posed or semiposed, respectively.

With the `/noisy` switch, other players in the room may be informed who you whisper to (but not what you whisper); the probability that a noisy whisper will be heard is set by the 'whisper_loudness' @config option. With the `/silent` switch, the whisper will not be overheard. (When neither switch is given, the default behaviour is controlled by the 'noisy_whisper' @config option.)

`<message>` will not be evaluated if the `/noeval` switch is given.

The `/list` switch lets you whisper to multiple people at once. In this case, `<players>` is a space-separated list of names, and names with spaces should be enclosed in double-quotes, as per page/list.


::: seealso
- [page]
- [:]
- [@pemit]
:::
# SESSION
`SESSION [<pattern>]`

The SESSION command is the same as the admin WHO, but instead of showing the host, it shows how many bytes each connection has moved: `Sent` is what the client sent to the game, `Recv` what the game sent to it, and `Pend` what has been written but not yet taken by the network. A connection whose counts the connection server did not report shows `-`. *<pattern>* limits the output, only showing players whose name begins with *<pattern>*, or whose names or aliases match *<pattern>* if it's a wildcard pattern.

Anyone who is not a wizard or royalty and lacks the See_All power gets the listing a mortal sees from [WHO] instead.


::: seealso
- [who]
:::
# with
`with[/room] <obj>=<command>`

Attempts to run a user-defined command on a specific object. If the `/room` switch is given, `<obj>` must be a room or your current location, and its contents are checked for commands as if it was a master room.

Output: the output of the command run. See [command output].

`<obj>` must be an object near you, an object you control, your ZMO or (if the `/room` switch is given) the Master Room.


::: seealso
- [$-commands]
- [evaluation order]
:::
# MSSP-REQUEST
`MSSP-REQUEST`

This socket command shows what the game reports to MUD crawlers and bots through the MUD Server Status Protocol (MSSP), as tab-separated `NAME<tab>value` lines between `MSSP-REPLY-START` and `MSSP-REPLY-END`. The MSSP telnet option sends the same report.

The server fills in what it knows: the game's name (`mud_name`), the players connected, when it started, its ports (`port`, `ssl_port`), its website (`mud_url`), the codebase, and the protocols it speaks. Everything else (genre, contact address, Discord, and so on) is the `mssp` configuration option, set from the portal's MSSP page under Configuration. A PennMUSH `mush.cnf` import carries over its `mssp name/value` lines.

A variable with several values repeats its name on one line per value, the default last. For the variables crawlers read, see https://tintin.mudhalla.net/protocols/mssp/


::: seealso
- [INFO]
:::
# @suggest
`@suggest[/list]`<br>
`@suggest/add <category>=<word>`<br>
`@suggest/delete <category>=<word>`

Given a list of known good words in a category, the mush can suggest ones based on misspelled or otherwise invalid words. This is used for suggesting function names, help entries, etc. @suggest provides a way to add custom categories and vocabulary words.

When given no switches or `/list`, shows all available suggestion categories. If the dict_file config option is set, the 'words' category is filled from that file at startup, and refilled whenever the file has changed since.

`/add` and `/delete` add or remove a word in a given category. They need the `config.admin` permission.

### Example
```sharp
> @suggest/add pets=dog
> @suggest/add pets=cat
> @suggest/add pets=bird
> think suggest(pets, birb)
BIRD
```


::: seealso
- [SUGGEST()]
:::


# @ps/history

`@ps/history [<limit>]`

Lists recent queue outcomes visible to your linked active character. The default is
50 entries, with a maximum of 100. Each line contains PID (or `-` for rejected work),
full source and owner identities, source attribute when known, kind, outcome, wait milliseconds, elapsed execution
milliseconds, and invocation count. Work cancelled before starting has no execution
measurement. This history contains no command bodies, arguments, register values,
results, or exception messages.

History is local to this server process: at most 1,024 records retained for 15 minutes.
Restarting clears it. Queue limits, cancelled work, execution limits, and failed work
have distinct outcomes. Enqueue/start/end timestamps in the portal are UTC labels;
wait and execution durations use a monotonic clock. Elapsed execution includes waits
for I/O and is not CPU time.

Access uses `queue.inspect.own` for work owned by your active character and
`queue.inspect` for other owners. An explicit own-scope denial cannot be bypassed
with the broader scope. Own history also requires the source and owner identities
to remain current and the character to control that source. The global scope can
inspect metadata for deleted sources. Permissions are checked on every request.

