# @LOCK-SIMPLE
# @LOCK-OBJID

**SIMPLE LOCKS**

You can lock an object in several different ways. The simplest lock is one that always succeeds (#true) or always fails (#false), or that matches a specific object by prefixing it with an "=":

```sharp
> @lock My Toy = #false
```
This lock will always fail.

```sharp
> @lock My Toy = =me
```
This locks the object "My Toy" to you and you alone. It is recommended that you `@lock me = =me` in order to prevent anyone else from picking you up. The two = signs are NOT a typo! The first is part of the @lock syntax (as shown at the top of [LOCKING]) the second is a lock key that means "only this exact object".

For backwards compatibility, `OBJID^<object>` is an alias for `=<object>`.

# @LOCK-OWNER
# @LOCK-CARRY

## OWNER LOCK

An "owner" lock allows you to lock something to anything owned by the same player:
```sharp
@lock Box = $My Toy
```
This locks "Box" to anything owned by the owner of "My Toy" (since players own themselves, that includes the owner as well).

## CARRY LOCK
You can lock an object to something that has to be carried:
```sharp
@lock Door = +Secret Door Key
```
This locks the exit "Door" to someone carrying the object "Secret Door Key". Anyone carrying that object will be able to go through the exit.

You can lock an object to -either- an object or to someone carrying the object with:
```sharp
@lock Disneyworld Entrance = Child
```
This locks the exit "Disneyworld Entrance" to either the object "Child" -or- to someone carrying the object "Child". (OK, so it's a weird example.)

This is the same as `@lock Entrance=+Child|=Child`.

# @LOCK-ATTRIBUTE

## ATTRIBUTE LOCKS
You can lock an object to an attribute on the person trying to pass the lock (as long as the object can "see" that attribute):

`@lock <object>=<attribute>:<value>`

*<value>* can contain wildcards (*), greater than (>) or less than (<) symbols.

For example:
```sharp
@lock Men's Room = sex:m*
```
This would lock the exit "Men's Room" to anyone with a SEX attribute starting with the letter "m".
```sharp
@lock A-F = icname:<g
```
This would lock the exit "A-F" to anyone with a ICNAME attribute starting with a letter "less than" the letter "g". This assumes that ICNAME is visual or the object with the lock can see it.

# @LOCK-NAME

## NAME LOCKS
You can test for objects matching a given name by using the below format:

`@lock <object>=name^<pattern>`

It is similar to performing strmatch(%n,*<pattern>*), though will also match for a player/exit with *<pattern>* as one of its @aliases.

For example, to lock "Bob's Tools" to only people with a name beginning with Bob:
```sharp
@lock/use Bob's Tools=name^bob*
```

# @LOCK-BIT
# @LOCK-FLAG
# @LOCK-TYPE
# @LOCK-POWER
# @LOCK-ROLE
# @LOCK-PERM
# @LOCK-CHANNEL

## BIT LOCKS
You can test for set flags, powers, roles, permissions, or object types in a lock directly, without using an evaluation lock, with these formats:

`@lock <object>=flag^<flag>`<br>
`@lock <object>=power^<power>`<br>
`@lock <object>=type^<type>`

These locks act like the object the lock is on does a hasflag(%#, *<flag>*), or haspower(%#, *<power>*), hastype(%#, *<type>*) succeeding only if the flag/power is set, or the object is of the specified type.

For example:
```sharp
@lock/use Admin Commands=flag^wizard|flag^royalty
```

To test for a role or a permission (see [roles]):

`@lock <object>=role^<role>`<br>
`@lock <object>=perm^<permission>`

`role^<role>` passes when the object trying the lock holds the role, its own or its account's, as hasrole() reports. `perm^<permission>` passes when it holds the permission, as permission() reports.

```sharp
@lock/use Staff Board=role^moderator|perm^players.moderate
```

You can also test for channel membership with:

`@lock <object>=channel^<channel>`

# @LOCK-DBREFLIST
# @LOCK-LIST

## LIST LOCK
You can test to see if the enactor is a member of a space-separated list of dbrefs or objids on an attribute on the object, with:

`@lock <object>=dbreflist^<attributename>`

For example:
```sharp
&allow Commands = #1 #7 #23 #200:841701384
&deny commands = #200 #1020
@lock/use commands = !dbreflist^deny & dbreflist^allow 
```

# @LOCK-INDIRECT

## INDIRECT LOCKS
An "indirect" lock allows you to lock something to the same thing as another object (very useful in setting channel locks; see [@CHANNEL CLOCK]):
```sharp
@lock Second Puppet=@First Puppet
```
This locks the object "Second Puppet" to whatever the object "First Puppet" is locked to. Normally, the lock type that is checked is the same as the lock on the first. You can specify a different lock type with @object/LOCKNAME. For example:
```sharp
@lock Second Puppet = @First Puppet/Use
```
Second Puppet's basic lock now checks First Puppet's use lock.

# @LOCK-HOST

## HOST LOCKS

You can check to make sure an object is owned by a player connected from a specific host or IP address using the following:

`@lock <object>=ip^<ipaddress>`<br>
`@lock <object>=hostname^<hostname>`

*<ipaddress>* and *<hostname>* can contain wildcards. *<object>* must be able to see the LASTIP attribute (for ip locks) or LASTSITE attribute (for hostname locks) on the enactor's owner.

For example:
```sharp
@lock <object>=ip^127.0.0.1
```
This locks *<object>* to players (and the objects of players) currently connected from the computer the MUSH is running on.


::: seealso
- [IPADDR()]
- [HOST()]
- [LASTSITE]
:::

# @LOCK/BASIC
# @LOCK/ENTER
# @LOCK/LEAVE
# @LOCK/TELEPORT

## Basic Lock
For exits, this lock controls who can pass through the exit.<br>
For players and things, it controls who can "get" the object.<br>
For rooms, it determines whether the @success or @failure verbs are triggered when someone "look"s at the room. However, even when the lock is failed, the "look" still occurs.


::: seealso
- [@asuccess]
- [@afailure]
- [go]
- [get]
- [look]
:::

## Enter Lock
For players and things, the Enter lock controls who can "enter" an ENTER_OK object, as well as who can "empty" it. It has no meaning for exits or rooms.


::: seealso
- [@aenter]
- [@aefail]
- [ENTER_OK]
- [enter]
- [empty]
:::

## Leave Lock
For players, things and rooms, the Leave lock controls who can leave the object, via "leave", "@teleport" or "goto". It has no meaning for exits.


::: seealso
- [@leave]
- [@lfail]
- [leave]
:::

## Teleport Lock
For rooms, the Teleport lock controls who can "@teleport" into the room, if it has the JUMP_OK flag set. It has no meaning for players, things or exits.


::: seealso
- [JUMP_OK]
- [@teleport]
- [LOCKING]
- [locktypes]
- [lock keys]
:::

# @LOCK/FOLLOW
# @LOCK/FORWARD
# @LOCK/DROPTO

## Follow Lock
For players and things, controls who may "follow" the object. Has no meaning for rooms or exits.


::: seealso
- [failure]
:::

## Forward Lock
For players, things and rooms, controls who can forward sound to an object, via @forwardlist or @debugforwardlist. Meaningless for exits.


::: seealso
- [@forwardlist]
- [@debugforwardlist]
- [@LOCK/PAGE]
:::

## Dropto Lock
For rooms, only objects which pass this lock will be sent to the rooms Drop-To. Has no meaning for players, things or exits.


::: seealso
- [DROP-TOS]
- [drop]
- [empty]
- [LOCKING]
- [locktypes]
- [lock keys]
:::

# @LOCK/USE
# @LOCK/COMMAND
# @LOCK/LISTEN

## Use Lock
For players, things and rooms, this lock controls who may "use" the object. You must also pass an object's Use lock to trigger $-commands or ^-listens on it (as well as the Command/Listen lock; see below). When an object is used as a Channel Mogrifier, only players who pass the object's Use lock will have their speech on the channel mogrified. Has no meaning for exits.


::: seealso
- [@ause]
- [@aufail]
- [use]
- [$-commands]
- [listening listen patterns]
- [@CHANNEL ADMIN]
:::

## Command Lock
For players, things and rooms, you must pass this lock (as well as the Use lock) to trigger $-commands on the object. Meaningless for exits.


::: seealso
- [$-commands]
- [failure]
:::

## Listen Lock
For players, things and rooms, you must pass this lock (as well as the Use lock) to trigger ^-listen patterns on the object when it's set MONITOR. Meaningless for exits.


::: seealso
- [listening listen patterns]
:::

# @LOCK/PAGE
# @LOCK/SPEECH
# @LOCK/MAIL
# @LOCK/MAILFORWARD
# @LOCK/INTERACT

## Page Lock
For players, things and rooms, you must pass this lock to page or @pemit to the object, or @remit inside it. Meaningless for exits.


::: seealso
- [failure]
- [@haven]
:::

## Speech Lock
Controls who can speak (via say, pose, @*emit or teach) inside an object. Meaningless for exits.


::: seealso
- [failure]
:::

## Mail Lock
Controls who can send @mail to this object.


::: seealso
- [MAIL]
- [failure]
:::

## Mailforward Lock
Controls who can forward @mail to this object via @mailforward.


::: seealso
- [MAIL]
- [@mailforward]
- [@LOCK/FOLLOW]
:::

## Interact Lock
Controls whose indirect speech you'll hear (from say, pose, channels, @emit, etc). Does not block sound directed specifically at you, such as page, whisper, @pemit, etc; use @lock/page for those. **Note**: if sound is blocked by the interact lock, the speaker will not be informed.

# @LOCK/DROP
# @LOCK/DROPIN
# @LOCK/GIVE
# @LOCK/FROM
# @LOCK/PAY
# @LOCK/RECEIVE
# @LOCK/TAKE

## Drop Lock
For players and things, controls who can drop the object. Has no meaning for exits. On rooms, has the same meaning as @lock/dropin.


::: seealso
- [drop]
- [empty]
:::

## Dropin Lock
When set on a player, thing or room, controls who can drop objects into them. Has no meaning for exits.

## Give Lock
For players and things, controls who may give the object away. Has no meaning for rooms or exits.

## From Lock
Controls who may give items to this object.

## Pay Lock
Controls who can 'buy' an item from this vendor.

## Receive Lock
Controls what may be given to this object.

## Take Lock
Controls who can take from this container.


::: seealso
- [give]
- [buy]
- [@LOCK/BASIC]
- [@LOCK/BASIC]
:::

# @LOCK/FILTER
# @LOCK/INFILTER

## Filter Lock
## Infilter Lock
These are lock versions of @filter and @infilter, respectively. Anyone who fails to pass the lock will have their speech filtered. The sound being made is passed to evaluation locks as %0.


::: seealso
- [@filter]
- [@infilter]
:::

# @LOCK/CONTROL
# @LOCK/DESTROY
# @LOCK/EXAMINE

## Control Lock
Allows objects which would not normally control something to do so. Does not work for players.


::: seealso
- [CONTROL]
:::

## Destroy Lock
Limits who can @destroy a DESTROY_OK object.


::: seealso
- [@destroy]
- [DESTROY_OK]
:::

## Examine Lock
Limits who can examine a VISUAL object.


::: seealso
- [examine]
- [VISUAL]
:::

# @LOCK/ZONE
# @LOCK/CHZONE
# @LOCK/CHOWN
# @LOCK/PARENT
# @LOCK/LINK
# @LOCK/OPEN

## Zone Lock
Objects which pass a SHARED player's @lock/zone control all the objects the shared player owns. If the zone_control_zmp_only @config option is off, anything passing the @lock/zone of other objects will control everything @chzoned to the object.


::: seealso
- [@chzone]
- [SHARED]
- [zones]
- [ZMR]
:::

## Chzone Lock
If set, controls who can @chzone an object to this zone.


::: seealso
- [@chzone]
- [zones]
:::

## Chown Lock
If set, controls who can change the owner of this CHOWN_OK object via @chown.


::: seealso
- [CHOWN_OK]
- [@chown]
:::

## Parent Lock
Controls who can @parent something to this LINK_OK object.


::: seealso
- [@parent]
- [LINK_OK]
:::

## Link Lock
Controls who can @link this unlinked exit, or who can @link an exit to this LINK_OK room/thing.


::: seealso
- [@link]
- [LINK_OK]
- [LINK_ANYWHERE POWER]
:::

## Open Lock
Controls who can @open an exit from this OPEN_OK room.


::: seealso
- [@open]
- [@dig]
- [OPEN_OK]
- [OPEN_ANYWHERE POWER]
:::

# @LOCK/USER
# @LOCK/USER:<NAME>

## User-defined Locks
User-defined locks have no hardcoded meaning. They allow you to set locks for any purpose, which you can test using the elock() function. *<name>* can be anything which is a valid attribute name. For example, in a combat system you might use a "wield" @lock on weapons, similar to:

```sharp
> @lock/user:wield War Hammer=strength:>20
```

and then test it with `elock(War Hammer/wield, %#)`.


::: seealso
- [ELOCK()]
- [valid()]
- [LOCKING]
- [locktypes]
- [lock keys]
:::
