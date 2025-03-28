# LOCK KEYS

There are many key types, and it is also possible to form more complex locks by using boolean symbols and grouping. See [help @lock-complex|@lock-complex] for examples.

The types of keys are outlined below. Detailed help for each is available by typing [help @lock-<key>|@lock-key], replacing *<key>* with the word on the left.

- **Simple** - Always true, always false, or locking to a specific object.
- **Name** - Check the name of the object attempting to pass the lock.
- **Owner** - Lock to objects owned by the owner of an object.
- **Carry** - Lock to someone carrying an object, such as a key.
- **Indirect** - Use the result of another @lock.
- **Attribute** - Check an attribute on the object trying to pass the lock.
- **Evaluation** - Evaluate an attribute on the object the lock is on.
- **Bit** - Check for a flag, type, power, or channel membership.
- **Dbreflist** - Check if the dbref of the object trying to pass the lock is in a list set in an attribute.
- **Host** - Check for players connecting from a particular host/ip.

You can negate lock keys, and combine multiple keys, as explained in [help lockkeys2|lockkeys2].

# LOCK KEYS2
# @LOCK-COMPLEX

A lock key can be negated by prefixing the key with an "!". For example:

```
> @lock North=flag^wizard
> @lock South=!flag^wizard
```

only lets those with the Wizard flag pass through the North exit, while only allowing those who do NOT have the Wizard flag to go South.

You can combine keys, either allowing someone to pass a lock if they pass any of the keys given, or requiring that they pass all of the keys, using the "|" (or) and "&" (and) symbols. For example:

```
> @lock OOC Room= status:OOC | power^guest
```

locks the exit "OOC Room" so that only those with their STATUS attribute set to "OOC", or those with the Guest @power, can pass, while

```
> @lock Men's Room= Sex:Male & +Bathroom Key
```

only allows those with their @sex set to Male who are carrying a "Bathroom Key" object to pass.

You can group together different sets of keys by enclosing each group in parenthesis "()". For instance,

```
> @lock Entrance=!type^player | (type^player & !flag^unregistered)
```

allows non-players to pass, or players who do not have the "unregistered" flag set.

See also:
- [help @lock|@lock]
- [help locktypes|locktypes]
- [help @clock|@clock]
- [help objid()|objid()]

# @LOCK-SIMPLE
# @LOCK-OBJID

## SIMPLE LOCKS

You can lock an object in several different ways. The simplest lock is one that always succeeds (#true) or always fails (#false), or that matches a specific object by prefixing it with an "=":

```
> @lock My Toy = #false
```
This lock will always fail.

```
> @lock My Toy = =me
```
This locks the object "My Toy" to you and you alone. It is recommended that you `@lock me = =me` in order to prevent anyone else from picking you up. The two = signs are NOT a typo! The first is part of the @lock syntax (as shown at the top of [help @lock|@lock]) the second is a lock key that means "only this exact object".

For backwards compatibility, `OBJID^<object>` is an alias for `=<object>`.

# @LOCK-OWNER
# @LOCK-CARRY

## OWNER LOCK

An "owner" lock allows you to lock something to anything owned by the same player:
```
@lock Box = $My Toy
```
This locks "Box" to anything owned by the owner of "My Toy" (since players own themselves, that includes the owner as well).

## CARRY LOCK
You can lock an object to something that has to be carried:
```
@lock Door = +Secret Door Key
```
This locks the exit "Door" to someone carrying the object "Secret Door Key". Anyone carrying that object will be able to go through the exit.

You can lock an object to -either- an object or to someone carrying the object with:
```
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
```
@lock Men's Room = sex:m*
```
This would lock the exit "Men's Room" to anyone with a SEX attribute starting with the letter "m".
```
@lock A-F = icname:<g
```
This would lock the exit "A-F" to anyone with a ICNAME attribute starting with a letter "less than" the letter "g". This assumes that ICNAME is visual or the object with the lock can see it.

# @LOCK-EVALUATION

## EVALUATION LOCK
An evaluation lock is set using this format:

`@lock <object>=<attribute>/<value>`

The difference between this and an attribute lock is that the *<attribute>* is taken from *<object>* rather than from the person trying to pass the lock. When someone tries, *<attribute>* is evaluated, and the result is compared to *<value>*. If it matches, then the person passes the lock.

The person trying to pass the lock is %# and *<object>* is %! when the evaluation takes place. The evaluation is done with the powers of *<object>*. If you try to do something (like [get(%#/*<attribute>*)]) and *<object>* doesn't have permission to do that, the person will automatically fail to pass the lock.

See also: [help @lock-eval2|@lock-eval2]

# @LOCK-EVAL2
# @LOCK-EVALUATION2

Example:
```
@lock Thursday Cafe = whichday/Thu
&whichday Thursday Cafe = first(time())
```
This locks the object "Thursday Cafe" (probably an exit) unless today is Thursday.

Whenever someone tries to pass through the exit, the attribute "whichday" will be evaluated, extracting the first word returned from time() (the day of the week). The result is compared with the value in the lock ("Thu"), and the lock will only be passable when the strings match--Only on Thursdays.

If you have an evaluation lock that just does [hasflag(%#,FLAGNAME)], you should probably use a bit lock instead.

See also: [help @lock-bit|@lock-bit]

# @LOCK-NAME

## NAME LOCKS
You can test for objects matching a given name by using the below format:

`@lock <object>=name^<pattern>`

It is similar to performing strmatch(%n,*<pattern>*), though will also match for a player/exit with *<pattern>* as one of its @aliases.

For example, to lock "Bob's Tools" to only people with a name beginning with Bob:
```
@lock/use Bob's Tools=name^bob*
```

# @LOCK-BIT
# @LOCK-FLAG
# @LOCK-TYPE
# @LOCK-POWER
# @LOCK-CHANNEL

## BIT LOCKS
You can test for set flags, powers, or object types in a lock directly, without using an evaluation lock, with these formats:

`@lock <object>=flag^<flag>`
`@lock <object>=power^<power>`
`@lock <object>=type^<type>`

These locks act like the object the lock is on does a hasflag(%#, *<flag>*), or haspower(%#, *<power>*), hastype(%#, *<type>*) succeeding only if the flag/power is set, or the object is of the specified type.

For example:
```
@lock/use Admin Commands=flag^wizard|flag^royalty
```

You can also test for channel membership with:

`@lock <object>=channel^<channel>`

# @LOCK-DBREFLIST
# @LOCK-LIST

## LIST LOCK
You can test to see if the enactor is a member of a space-separated list of dbrefs or objids on an attribute on the object, with:

`@lock <object>=dbreflist^<attributename>`

For example:
```
&allow Commands = #1 #7 #23 #200:841701384
&deny commands = #200 #1020
@lock/use commands = !dbreflist^deny & dbreflist^allow 
```

# @LOCK-INDIRECT

## INDIRECT LOCKS
An "indirect" lock allows you to lock something to the same thing as another object (very useful in setting channel locks; see [help @clock|@clock]):
```
@lock Second Puppet=@First Puppet
```
This locks the object "Second Puppet" to whatever the object "First Puppet" is locked to. Normally, the lock type that is checked is the same as the lock on the first. You can specify a different lock type with @object/LOCKNAME. For example:
```
@lock Second Puppet = @First Puppet/Use
```
Second Puppet's basic lock now checks First Puppet's use lock.

# @LOCK-HOST

## HOST LOCKS

You can check to make sure an object is owned by a player connected from a specific host or IP address using the following:

`@lock <object>=ip^<ipaddress>`
`@lock <object>=hostname^<hostname>`

*<ipaddress>* and *<hostname>* can contain wildcards. *<object>* must be able to see the LASTIP attribute (for ip locks) or LASTSITE attribute (for hostname locks) on the enactor's owner.

For example:
```
@lock <object>=ip^127.0.0.1
```
This locks *<object>* to players (and the objects of players) currently connected from the computer the MUSH is running on.

See also:
- [help ipaddr()|ipaddr()]
- [help hostname()|hostname()]
- [help LASTSITE|LASTSITE]

# LOCKTYPES
# LOCKLIST
# LOCK TYPES
# LOCK LIST

These are the standard lock types supported by SharpMUSH. For more detailed information about any lock type, see [help @lock/<lock>|@lock-lock].

Standard Lock Types:
- `@lock/basic` - Who can pick up the player/thing, or go through the exit.
- `@lock/enter` - Who can enter the player/object (aka @elock)
- `@lock/teleport` - Who can teleport to the room
- `@lock/use` - Who can use the object (aka @ulock)
- `@lock/page` - Who can page/@pemit the player
- `@lock/zone` - Who can control objects on this zone
- `@lock/parent` - Who can @parent something to this object/room
- `@lock/link` - Who can @link something to this object/room or who can @link this unlinked exit.
- `@lock/open` - Who can @open an exit from this room
- `@lock/mail` - Who can @mail the player
- `@lock/user:<name>` - User-defined. No built-in function of this lock, but users can test it with elock()

See also: [help locktypes2|locktypes2]

# LOCK TYPES2
# LOCKTYPES2

More standard lock types:

- `@lock/speech` - Who can speak/pose/emit in this room
- `@lock/listen` - Who can trigger my @ahear/^-pattern actions
- `@lock/command` - Who can trigger my $-pattern commands
- `@lock/leave` - Who can leave this object (or room, via exits/@tel)
- `@lock/drop` - Who can drop this object
- `@lock/dropin` - Who can drop objects into this location.
- `@lock/give` - Who can give this object
- `@lock/from` - Who can give things to this object
- `@lock/pay` - Who can give pennies to/buy from this object
- `@lock/receive` - What things can be given to this object
- `@lock/follow` - Who can follow this object
- `@lock/examine` - Who can examine this object if it's VISUAL
- `@lock/chzone` - Who can @chzone to this object if it's a ZMO
- `@lock/forward` - Who can @forwardlist a message to this object 
- `@lock/filter` - Controls if the message %0 should be filtered
- `@lock/infilter` - Controls if the message %0 should be infiltered
- `@lock/control` - Who can control this object (only if set; non-player)
- `@lock/dropto` - Who can trigger this container's drop-to.
- `@lock/destroy` - Who can destroy this object if it's DESTROY_OK
- `@lock/interact` - Who can send sound (say/pose/emit/etc) to this object
- `@lock/take` - Who can get things contained in this object
- `@lock/mailforward` - Who can forward mail to this object via @mailforward
- `@lock/chown` - Who can @chown this CHOWN_OK object?

See also:
- [help @lock|@lock]
- [help @lset|@lset]
- [help @clock|@clock]
- [help FAILURE|FAILURE]

# @LOCK/BASIC
# @LOCK/ENTER
# @LOCK/LEAVE
# @LOCK/TELEPORT

## Basic Lock
For exits, this lock controls who can pass through the exit.
For players and things, it controls who can "get" the object.
For rooms, it determines whether the @success or @failure verbs are triggered when someone "look"s at the room. However, even when the lock is failed, the "look" still occurs.

See also:
- [help @success|@success]
- [help @failure|@failure]
- [help goto|goto]
- [help get|get]
- [help look|look]

## Enter Lock
For players and things, the Enter lock controls who can "enter" an ENTER_OK object, as well as who can "empty" it. It has no meaning for exits or rooms.

See also:
- [help @enter|@enter]
- [help @efail|@efail]
- [help ENTER_OK|ENTER_OK]
- [help enter|enter]
- [help empty|empty]

## Leave Lock
For players, things and rooms, the Leave lock controls who can leave the object, via "leave", "@teleport" or "goto". It has no meaning for exits.

See also:
- [help @leave|@leave]
- [help @lfail|@lfail]
- [help leave|leave]

## Teleport Lock
For rooms, the Teleport lock controls who can "@teleport" into the room, if it has the JUMP_OK flag set. It has no meaning for players, things or exits.

See also:
- [help JUMP_OK|JUMP_OK]
- [help @teleport|@teleport]
- [help @lock|@lock]
- [help locktypes|locktypes]
- [help lockkeys|lockkeys]

# @LOCK/FOLLOW
# @LOCK/FORWARD
# @LOCK/DROPTO

## Follow Lock
For players and things, controls who may "follow" the object. Has no meaning for rooms or exits.

See also: [help FAILURE|FAILURE]

## Forward Lock
For players, things and rooms, controls who can forward sound to an object, via @forwardlist or @debugforwardlist. Meaningless for exits.

See also:
- [help @forwardlist|@forwardlist]
- [help @debugforwardlist|@debugforwardlist]
- [help @lock/mailforward|@lock/mailforward]

## Dropto Lock
For rooms, only objects which pass this lock will be sent to the rooms Drop-To. Has no meaning for players, things or exits.

See also:
- [help DROP-TOS|DROP-TOS]
- [help drop|drop]
- [help empty|empty]
- [help @lock|@lock]
- [help locktypes|locktypes]
- [help lockkeys|lockkeys]

# @LOCK/USE
# @LOCK/COMMAND
# @LOCK/LISTEN

## Use Lock
For players, things and rooms, this lock controls who may "use" the object. You must also pass an object's Use lock to trigger $-commands or ^-listens on it (as well as the Command/Listen lock; see below). When an object is used as a Channel Mogrifier, only players who pass the object's Use lock will have their speech on the channel mogrified. Has no meaning for exits.

See also:
- [help @use|@use]
- [help @ufail|@ufail]
- [help use|use]
- [help $-commands|$-commands]
- [help ^|^]
- [help MOGRIFY|MOGRIFY]

## Command Lock
For players, things and rooms, you must pass this lock (as well as the Use lock) to trigger $-commands on the object. Meaningless for exits.

See also:
- [help $-commands|$-commands]
- [help FAILURE|FAILURE]

## Listen Lock
For players, things and rooms, you must pass this lock (as well as the Use lock) to trigger ^-listen patterns on the object when it's set MONITOR. Meaningless for exits.

See also: [help ^|^]

# @LOCK/PAGE
# @LOCK/SPEECH
# @LOCK/MAIL
# @LOCK/MAILFORWARD
# @LOCK/INTERACT

## Page Lock
For players, things and rooms, you must pass this lock to page or @pemit to the object, or @remit inside it. Meaningless for exits.

See also:
- [help FAILURE|FAILURE]
- [help @haven|@haven]

## Speech Lock
Controls who can speak (via say, pose, @*emit or teach) inside an object. Meaningless for exits.

See also: [help FAILURE|FAILURE]

## Mail Lock
Controls who can send @mail to this object.

See also:
- [help @mail|@mail]
- [help FAILURE|FAILURE]

## Mailforward Lock
Controls who can forward @mail to this object via @mailforward.

See also:
- [help @mail|@mail]
- [help @mailforward|@mailforward]
- [help @lock/forward|@lock/forward]

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

See also:
- [help drop|drop]
- [help empty|empty]

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

See also:
- [help give|give]
- [help buy|buy]
- [help @lock/basic|@lock/basic]
- [help @lock/enter|@lock/enter]

# @LOCK/FILTER
# @LOCK/INFILTER

## Filter Lock
## Infilter Lock
These are lock versions of @filter and @infilter, respectively. Anyone who fails to pass the lock will have their speech filtered. The sound being made is passed to evaluation locks as %0.

See also:
- [help @filter|@filter]
- [help @infilter|@infilter]

# @LOCK/CONTROL
# @LOCK/DESTROY
# @LOCK/EXAMINE

## Control Lock
Allows objects which would not normally control something to do so. Does not work for players.

See also: [help CONTROL|CONTROL]

## Destroy Lock
Limits who can @destroy a DESTROY_OK object.

See also:
- [help @destroy|@destroy]
- [help DESTROY_OK|DESTROY_OK]

## Examine Lock
Limits who can examine a VISUAL object.

See also:
- [help examine|examine]
- [help VISUAL|VISUAL]

# @LOCK/ZONE
# @LOCK/CHZONE
# @LOCK/CHOWN
# @LOCK/PARENT
# @LOCK/LINK
# @LOCK/OPEN

## Zone Lock
Objects which pass a SHARED player's @lock/zone control all the objects the shared player owns. If the zone_control_zmp_only @config option is off, anything passing the @lock/zone of other objects will control everything @chzoned to the object.

See also:
- [help @chzone|@chzone]
- [help SHARED|SHARED]
- [help ZONES|ZONES]
- [help ZMR|ZMR]

## Chzone Lock
If set, controls who can @chzone an object to this zone.

See also:
- [help @chzone|@chzone]
- [help ZONES|ZONES]

## Chown Lock
If set, controls who can change the owner of this CHOWN_OK object via @chown.

See also:
- [help CHOWN_OK|CHOWN_OK]
- [help @chown|@chown]

## Parent Lock
Controls who can @parent something to this LINK_OK object.

See also:
- [help @parent|@parent]
- [help LINK_OK|LINK_OK]

## Link Lock
Controls who can @link this unlinked exit, or who can @link an exit to this LINK_OK room/thing.

See also:
- [help @link|@link]
- [help LINK_OK|LINK_OK]
- [help LINK_ANYWHERE POWER|LINK_ANYWHERE POWER]

## Open Lock
Controls who can @open an exit from this OPEN_OK room.

See also:
- [help @open|@open]
- [help @dig|@dig]
- [help OPEN_OK|OPEN_OK]
- [help OPEN_ANYWHERE POWER|OPEN_ANYWHERE POWER]

# @LOCK/USER
# @LOCK/USER:<NAME>

## User-defined Locks
User-defined locks have no hardcoded meaning. They allow you to set locks for any purpose, which you can test using the elock() function. *<name>* can be anything which is a valid attribute name. For example, in a combat system you might use a "wield" @lock on weapons, similar to:

```
> @lock/user:wield War Hammer=strength:>20
```

and then test it with `elock(War Hammer/wield, %#)`.

See also:
- [help elock()|elock()]
- [help valid()|valid()]
- [help @lock|@lock]
- [help locktypes|locktypes]
- [help lockkeys|lockkeys]