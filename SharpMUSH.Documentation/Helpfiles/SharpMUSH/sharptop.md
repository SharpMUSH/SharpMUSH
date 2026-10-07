# Help
This is the index to the MUSH online help files.

  For an explanation of the help system, type:    help [newbie]<br>
  For a walkthrough of SharpMUSH systems, type:   help [Getting Started]<br>
  For help finding the helpfile you want:         help [help search]

  For the list of MUSH commands, type:            help [COMMANDS]<br>
  For the list of MUSH topics, type:              help [topics]<br>
  For an alphabetical list of all help entries:   help [help search]<br>
  For what SharpMUSH adds to PennMUSH:            help [sharpmush features]<br>
  For information about SharpMUSH:                help [code]

  For a list of flags:                            help [FLAG LIST]<br>
  For a list of functions:                        help [FUNCTION LIST]<br>
  For a list of attributes:                       help [attributes]<br>
  To see the configuration of this MUSH:          `@config`

  On many MUSHes, list local commands with:       `+help`

  If there are any errors in the help text, please notify a wizard in the game, or file an issue at https://github.com/SharpMUSH/SharpMUSH/issues, which is the bug-tracking site for SharpMUSH (and its distributed help files) but probably has no relation to this MUSH in particular.

# Getting Started
# GS
# Walkthrough
  This helpfile is a quick walkthrough of some of SharpMUSH's standard systems. It uses the same syntax as the other helpfiles; if you're not familiar with the syntax of the SharpMUSH helpfiles, please read [newbie] first, as it's explained there.

  For help with getting around, please see [gs moving].

  To talk to people in the room with you, see [gs talking].

  For a brief guide to the SharpMUSH chat system, see [gs chat].

  For information on how to send and read mail using SharpMUSH's built-in mail system, `@mail`, see [gs mail].

  If the game has a web portal, you can [register] an account there or at the login screen, then [make] characters and [play] them. The portal also serves the game's [wiki].

  To have the server address you in another language, see [@locale].

  For the other things SharpMUSH can do, see [sharpmush features].

# Ancestors
  Objects can inherit attributes and locks from other objects through the use of parents. An object's parent, its parent's parent, its parent's parent's parent, etc. constitute the object's "parent chain" and lookups work the way up the chain until an inheritance occurs.

  Ancestors are "virtual parents" that are assumed to be last on every parent chain. There is one ancestor for each object type (room, exit, thing, player), and `@config` lists the dbref of each ancestor object (`@config` ancestor_room, etc). Under normal circumstances, if an attribute/lock can't be retrieved from an object or any of its explicit parents, the attribute will be looked for on the appropriate ancestor. The ORPHAN flag may be set on an object to cause lookups on that object to ignore ancestors (like the pre-ancestor behavior).

  Ancestors may themselves have parent chains, but these are (obviously) not virtually terminated by ancestors.

  Note that the choice of which ancestor to look up is based on the type of the *child* object, as is the check of the ORPHAN flag. Also note that ancestors are *not* checked for `$-commands` or ^-commands; you should use the master room for global commands, instead.


::: seealso
- [parent]
- [ORPHAN]
:::

# Attribute Ownership
# Attrib-ownership
  The latest person to set an attribute on an object is the owner of that attribute. If you lock an attribute, using the `@atrlock` command, only the person who owns the attribute will be able to alter the attribute. This allows you to create standard commands on objects and then `@chown` them to others without letting them alter them.

  Attribute ownership is NOT changed when the object itself is `@chown`'ed. To change attribute ownership, you must use the `@atrchown` command.

  You must control an object in order to set attributes on it.

::: seealso
- [@atrlock]
- [@atrchown]
- [OWNER()]
- [attributes]
:::

# Clients
  Clients are special software programs that you can use to connect to MUSHes. They are usually much nicer to use than raw telnet and give you many additional features, such as larger text buffers (so you can type more), backscroll, history of previous commands, macros, and so on.

  Here is a list of common clients and the web sites where they can be found.  Please note that the below sites are subject to change. The below are listed solely for your information and possible benefit. The developers of SharpMUSH have nothing to do with the clients. Except for Potato, which is made by Mike. Not that this is a shameless plug. Noooo. Carry on.

  | OPERATING SYSTEM | CLIENT      | WEB SITE                              |
  |------------------|-------------|---------------------------------------|
  | UNIX             | Tinyfugue   | http://tinyfugue.sourceforge.net      |
  |                  | Potato      | http://www.potatomushclient.com       |
  | WINDOWS          | MUSHClient  | http://www.mushclient.com             |
  |                  | MuckClient  | http://www.xcalibur.co.uk/MuckClient/ |
  |                  | Potato      | http://www.potatomushclient.com       |
  |                  | BeipMU      | http://www.beipmu.com (Recommended)   |
  | MAC OS X         | Savitar     | http://www.heynow.com/Savitar/        |
  |                  | Atlantis    | http://www.riverdark.net/atlantis/    |
  |                  | Potato      | http://www.potatomushclient.com       |
  |                  | Unix clients will also run on OS X. |               |

# Control
  Controlling an object basically means that you have the power to change the object's characteristics such as flags and attributes. It may also mean that you have the ability to destroy it.

  These checks are performed, from top to bottom, to see if object O controls object V:
   1. If O has the Guest power, O does not control V
   2. If O and V are the same object, O controls V
   3. If V is God, O does not control V
   4. If O is a Wizard, O controls V
   5. If V is a Wizard, O does not control V
   6. If V is Royalty and O is not, O does not control V
   7. If O is MISTRUST, O does not control V
   8. If O and V are owned by the same player, and either:
       a. V is not set TRUST, or<br>
       b. O is set TRUST<br>
      then O controls V
   9. If V is a player, or set TRUST, O does not control V
  10. If the zone_control_zmp_only `@config` option is set to 'No', V is on a Zone, and O passes the Zone's `@lock`/zone, O controls V
  11. If V is owned by a SHARED player, and O passes the owner's `@lock`/zone, O controls V
  12. If V has an `@lock`/control, and O passes the lock, O controls V
  13. O does not control V


::: seealso
- [CONTROLS()]
- [TRUST]
- [MISTRUST]
- [zones]
- [zone masters]
:::

# Costs
  Some things on the MUSH cost pennies. The default costs are shown below:

    `@dig`: 10 pennies<br>
    `@create`: 10 pennies (or more)<br>
    `@search`: 100 pennies *<br>
    `@link`: 1 penny (if you didn't already own it, +1 to the previous owner)<br>
    `@open`: 1 penny (2 pennies if linked at the same time)

  Type '`@config`/list costs' to get the costs for the MUSH you are on.


::: seealso
- [MONEY]
- [MONEY()]
- [score]
:::

# Credits
  Developers: Mercutio (Harry Cordewener)

  Big thanks to the developers of PennMUSH and all of the history it is derived from, as well as most of these helpfiles.

::: seealso
- [code]
- [copyright]
:::

# Object IDs
# Objids
  When an object is destroyed, its dbref number will eventually be recycled and given to a newly created object. This can cause problems in code, particularly in database code which stores members of a group, as code which was meant to refer to the old object ends up referring to the new one by mistake.

  To avoid this problem, you can use the "object id", or objid, instead of the dbref. An object id consists of the object's dbref, a colon, and then the object's creation time. Objids can be used anywhere dbrefs can and, because the creation time is different each time the dbref is recycled, the objid is totally unique to each object.

  The `objid()` function returns the object id of an object, and the %: substitution evaluates to the objid of the enactor.


::: seealso
- [OBJID()]
- [@LOCK-SIMPLE]
- [database]
:::

# Drop-tos
# Droptos
  When you use the `@link` command on a room, it sets another room or object as the DROP-TO location. By default, any non-STICKY object that someone drops in the room will automatically be transported to the drop-to location, rather than staying in the room. Any STICKY object dropped in the room will go to its home.

  If the room is set STICKY, objects will stay in the room until the last player leaves or disconnects, at which point they will be transported as described above.

  Drop-tos work on things and players alike.

  If the room has a `@lock/dropto` set on it, only objects that pass the lock will be transported (either immediately or when the last player leaves if the room is STICKY). This can be used to prevent the dropto from acting on, say, objects containing connected players.

::: seealso
- [@link]
- [STICKY]
- [LINK_OK]
- [LOCKING]
:::

# Enactor
# %#
# %n
# %k
# %~
# %:
  The enactor is the object which causes something to happen. This is an important concept in MUSH, because the way many commands work will depend on who enters the command (ie, who the enactor is). Note that while the enactor is the object which -caused- code/a command to run, it is not necessarily the object which is running the code (that's the executor). Any type of object can be an enactor.

  There are eight %-substitutions that involve the enactor:<br>
    %# = the enactor's dbref<br>
    %n = the enactor's name<br>
    %~ = the enactor's accented name<br>
    %k = the enactor's name, colored by their `@moniker` (if any)<br>
    %: = the enactor's unique identifier, like `objid(%#)`<br>
    %a, %o, %p, %s = pronoun substitutions, by default based on the enactor's `@sex`. See [GENDER] for more information.

  If, for example, you have an `@osuccess` on an object that includes the %n subtitution, whenever someone picks up the object, that %n will evaluate to the name of the enactor (the person who typed 'get `<object>`' in this case).

::: seealso
- [%!]
- [%@]
- [%]
- [database]
:::

# Executor
# %!
  The executor of a command is the object actually carrying out the command or running the code. This differs from the enactor, because the enactor is the object that sets off the command. In some cases, the enactor and the executor will be the same. The substitution %! evaluates to the dbref of the executor of the code.

  For example:
```sharp
    > @emit %n (%#) is the enactor and %! is the executor!
    Cyclonus (#6) is the enactor and #6 is the executor!
    > @create Box
    Created: Object #10
    > &EMIT box=$emit: @emit %n (%#) is the enactor and %! is the executor!
    > emit
    Cyclonus (#6) is the enactor and #10 is the executor!
```

  In the first case, Cyclonus directly entered the command and was therefore both the enactor and the executor. In the second, Cyclonus set off the command on the box, so Cyclonus was still the enactor, but the box was the object that was actually doing the `@emit`, and was thus the executor.

::: seealso
- [%#]
- [%@]
- [%]
:::

# Caller
# %@
  The caller is the object which causes an attribute to be evaluated (for instance, by using `ufun()` or a similar function). The substitution %@ evaluates to the caller's dbref. It's particularly useful for functions with side-effects, to check that the object evaluating the function has permission.

  Example:
```sharp
    > &cmd_test Foo=$test: @emit ufun(Bar/fun_test)
    > &fun_test Bar=%n(%#) typed 'test', and [name(%@)](%@) ufun()'d this!
    > test
    Mike(#5) typed 'test', and Foo(#6) ufun()'d this!
```

```sharp
    > &wizfun Foo=if(hasflag(%@, Wizard), ufun(wizfun2), #-1 Sorry)
```

::: seealso
- [%#]
- [%!]
:::

# Gender
# SEX
  Gender on a MUSH is entirely up to you. You can set yourself (or any of your objects) to be male, female, neuter, or plural. If whatever is in the SEX attribute is not recognizable, the MUSH will assume the object is neuter. Setting a gender attribute will enable pronoun substitution by the MUSH. The SEX attribute is visual to anyone who wants to see it.

  The `obj()`, `subj()`, `poss()` and `aposs()` functions return different pronouns for an object based on its `@sex`, and the %o, %s, %p and %a substitutions return the same pronouns for the enactor.

::: seealso
- [@sex]
- [%]
:::

# Globals
# Global Commands
  A command is "global" if it can be used anywhere in the world of the MUSH. The standard/built-in MUSH commands are all global, so this term is usually used to refer to user-defined commands on objects in the Master Room of the MUSH. Global commands very greatly from MUSH to MUSH, but you can usually find MUSH-specific help on them by typing "`+help`".

::: seealso
- [MASTER ROOM]
- [$-commands]
- [evaluation order]
:::

# here
  The word 'here' refers to the room you are in. For example, to rename the room you're in (if you control it), you could use:

```sharp
    > @name here=<new name>
```

::: seealso
- [MATCHING]
:::

# Homes
# Home
  Every thing or player has a home, which is usually the room where it was created. You can reset your home or the home of any object you own with the `@link` command: `@link` [me | `<object>`]=`<location>`. You must also control `<location>`, unless that location (room or thing) is set ABODE or LINK_OK.

  The 'home' command outputs the dbref of the room you arrive in (see [command output]).

  When a player types 'home', she is sent back to the home room. When a thing with the STICKY flag set on it is dropped, it also goes to its home location. Note that if the FIXED flag is set on a player, she cannot use the 'home' command.

  You can create an exit that sends players home by doing:
```sharp
    > @link <exit name>=home
  You can set the drop-to in a room to home by doing:
    > @link <room dbref or "here">=home
```

  The home of an exit is its source (the room it's located in). You can change the home/source of an exit by `@teleporting` it to another room.

  The home of a room is its drop-to.

::: seealso
- [DROP-TOS]
- [@link]
- [STICKY]
- [LINK_OK]
- [FIXED]
- [EXITS]
- [HOME()]
- [LOC()]
:::

# LAST
# LASTLOGOUT
  LAST and LASTLOGOUT

  These attributes show the last times you connected and disconnected from the MUSH.

::: seealso
- [LASTSITE]
:::

# LASTSITE
# LASTIP
  LASTSITE and LASTIP

  The LASTSITE attribute gives the name of the site you last connected from. The LASTIP attribute gives the IP address you last connected from. Mortals cannot set them.

::: seealso
- [LAST]
:::

# Linking

  You can link to a room if you control it, or if it is set LINK_OK or ABODE. Being able to link means you can set the homes of objects or yourself to that room if it is set ABODE, and can set the destination of exits to that room if it is LINK_OK.

::: seealso
- [LINK_OK]
- [ABODE]
- [@link]
:::

# Lists
  The word "list" is used in the help files to refer to a string that is a series of smaller strings separated by one or more spaces. A list can also have its elements separated by some other kind of character -- the separating character is called the "delimiter". For example, the following are all lists:

    #6 #10 #14 #12
    Rumble|Krystal|Bobatron|Rodimus Prime   ('|' is the delimiter here)<br>
    foo bar whee blarg<br>
    -eek- .boing. yawp #5 7

  Lots of MUSHCode depends on lists and manipulating them. Normally, a<br>
  list is made up of similar items (so the fourth list in the example<br>
  is NOT a typical one).

::: seealso
- [STRINGS]
- [List functions]
- [action lists]
:::

# Looping
  Looping in an object can have its good parts and its bad parts. The good part is when you activate part of a program multiple times to exhaustively perform an operation. This can be done like this:

  ```sharp
    &PART1 object=<action list> ; @trigger me/PART2
    &PART2 object=@select <test for being done>=<false>,@trigger me/PART1
  ```
  Looping can be a problem when it goes on without stopping. The `@ps` command can be used to see if you are looping. Beware! A looping machine that isn't `@halt`'d will drain your pennies while you are away from the mush!

  The `@retry` and `@include` commands, and %= substitution, can also be useful for building code which needs to loop.

::: seealso
- [@ps]
- [HALTED]
- [COSTS]
- [@trigger]
- [@retry]
- [% substitution reference]
:::

# Master Room

  The Master Room enables global commands and exits. Exits in the Master Room may be used from any location on the MUSH. All objects left in the Master Room are checked for user-defined `$-commands`. Those `$-commands` are considered global, meaning that they can be used anywhere on the MUSH. Normally, only wizards will have access to the Master Room; if you have a global command that you would like to see enabled for the MUSH, speak to a wizard.

::: seealso
- [evaluation order]
- [GLOBALS]
:::

# me
  The word 'me' refers to yourself. Some things to do when starting out:<br>
  1) give yourself a description:       `@desc` me=`<description>`<br>
  2) check your desc.:                  look me<br>
  3) lock yourself:                     `@lock` me==me<br>
  4) set your gender:                   `@sex` me=`<male|female|neuter|plural>`

::: seealso
- [newbie]
- [LOCKING]
- [@describe]
- [@sex]
- [MATCHING]
:::

# Money
  The MUSH has a built-in money system, which gives a starting amount of money to new players and hands out a daily allowance thereafter. MUSH money (the default name is "pennies", but this may be different depending on the particular MUSH) is spent on some MUSH commands that are computationally expensive or alter the database. In addition, every time you "queue" a command, it costs you a certain amount of money -- this prevents looping from getting out of control, since when all your money is spent, you can't queue any more commands.

  The money system can also be used on player-created objects by setting `@cost`/`@payment`/`@opayment`/`@apayment` attributes and using the "give" command to give pennies, or by setting `@pricelist`/`@buy`/`@obuy`/`@abuy` attributes and buying items with the "buy" command. The Pay `@lock` on an object controls who can give it pennies.

  The "score" command tells you how many pennies you have.


::: seealso
- [COSTS]
- [give]
- [@cost]
- [@apayment]
- [LOCKING]
- [buy]
- [@buy]
- [score]
- [MONEY()]
:::

# Non-standard Attributes
  While there are many standard attributes in MUSH, objects can also have an enormous number of attributes, with any name you wish to use. In the past, you were limited to attributes named VA-VZ, WA-WZ, XA-XZ; these are still available as standard attributes. However, it is strongly recommended that you use non-standard attributes and meaningful names in order to make maintaining your MUSHCode easier.

  To set a non-standard attribute, you can use these formats:<br>
      &`<attribute name>` `<obj>`=`<value>`  OR<br>
      `@_`<attribute_name> `<obj>`=`<value>` OR<br>
      `@set` `<obj>`=`<attribute_name>`:`<value>`

  You can get the value of attributes using the functions `v()`, `get()`, and `xget()`. You can evaluate attributes using `u()`, `eval()`, and `get_eval()`. All attributes can be used in attribute locks and can be 'owned' independent of object ownership.


::: seealso
- [attributes]
- [ATTRIB-OWNERSHIP]
- [Attribute functions]
- [attribute trees]
- [attribute flags]
:::

# Quotas
  The Quota system controls how many objects a player may own. It is only used of the 'use_quota' `@config` option is set to Yes.

  Each object created normally costs one quota (though this can be altered with the 'quota_cost' `@config` option), and every player starts with a fixed amount of quota, controlled with the 'starting_quota' `@config` option. Whenever you create an object of any type, your remaining quota, stored in the RQUOTA attribute, is decreased by the quota_cost.

  Wizards can give specific players (for example, builder characters) unlimited quota by giving them the No_Quota `@power`.

  You can view your current quota with the `@quota` command. Wizards can adjust a specific player's quota qith the `@squota` command, and God can see or alter all players' quotas with `@allquota`.


::: seealso
- [@quota]
- [QUOTA()]
- [@power]
- [@power]
:::

# Regexp Examples
  The regexp pattern '.' is equivalent to the wildcard '?'; it matches one and only one of an arbitrary character.

  The regexp pattern '.*' is equivalent to the wildcard '*'; it matches zero or more arbitrary characters. To match one or more arbitrary characters, the regexp pattern is '.+'.

  To match a string of numbers, use:       [0-9]+    or \d+<br>
  To match a string of letters only, use:  [A-Za-z]+ or \w+

  See [regexp syntax]

# RQUOTA
  This attribute tracks remaining building quota if it is implemented. It is settable in-game only by a wizard, and is only visible to wizards.


::: seealso
- [@quota]
- [@quota administrative quota changes]
:::

# Setting Attributes
# Setting-attributes
  Standard attributes are set using @`<attrib>` `<obj>`=`<value>`<br>
  Nonstandard attributes are set using &`<attrib>` `<obj>`=`<value>`<br>
  Attributes may also be set using `@set` `<obj>`=`<attrib>`:`<value>` or the `attrib_set()` or `set()` functions.

  Attributes are cleared using @`<attrib>` `<obj>` or &`<attrib>` `<obj>`, `attrib_set()`, or with `@wipe` or `wipe()`.

  Note that if the empty_attrs configuration option is set (`@config` empty_attrs to check), there is a difference between clearing an attribute and setting an attribute to a null value:<br>
    `@va` me       <--- wipes out my VA attribute<br>
    `@va` me=      <--- sets my VA attribute to be empty

  Empty attributes retain their flags and atrlock status. Wiped attributes are gone forever.


::: seealso
- [attributes]
- [NON-STANDARD ATTRIBUTES]
- [@set]
- [@wipe]
- [ATTRIB_SET()]
- [SET()]
- [WIPE()]
- [attribute flags]
:::

# Spoofing
  Spoofing is the act of making other characters think that a person said or did something that they did not. This is very easy to accomplish, and has some good effects, which is why it is allowed. However, abusing it is very twinkish and will most likely get you in hot water with your wizards. Note that if you are being spoofed and want to know who is doing it, you can set yourself NOSPOOF and you will be notified who is making the `@emits`.

  Most @*emit commands have a wizard-only `@ns`*emit version, which does not show nospoof information. These are useful for writing global commands.

  Some @*emit commands also take a /spoof switch, which causes nospoof information to show that sound originated from the enactor (%#) instead of the executor (%!). This switch allows staff to write global `$-commands` for speech which show more helpful nospoof tags.


::: seealso
- [@emit]
- [@pemit]
- [@remit]
- [@oemit]
- [NOSPOOF]
- [PARANOID]
:::

# Stack
  For those unfamiliar with the term stack, it refers to a programming data structure that follows a LIFO (Last-In-First-Out) principle. The stack in MUSH holds the REGISTERS. The first ten registers can be accessed via the %0-%9 %-substitutions and v(0)-v(29), while all 30 registers can be accessed via the `r()` function (r(0, args) up to v(29,args)).

  When using regexp `$-commands` with named subpatterns, the named arguments can be accessed via r(`<name>`, args).


::: seealso
- [registers]
- [R()]
- [V()]
:::

# Strings
  A string is simply a bunch of characters. A word is a string that begins and ends with the space character. A sentence is a string made up of smaller substrings that are words. Please note that a "word" or "sentence" in this technical sense does not have to make sense in English (or in any other language, for that matter). As far as mush functions and commands are concerned, this is a perfectly good sentence:

        Foozle 09blert bar baz foo.


::: seealso
- [String functions]
:::

# Success
  A "success" normally occurs when you attempt to do something that is restricted by an `@lock` and you pass the `@lock`. (Note that if no lock is set, you automatically pass it.) For example, the "basic" lock restricts who can pick up a player/thing or who can go through an exit. Whenever you successfully do either of these things, you will set off the basic success messages on the object whose lock you have just successfully passed.

  Many other actions can also be locked - see `@lock` and locktypes for more information. Many of these actions have standard attributes that you can set messages in for when someone succeeds.


::: seealso
- [failure]
- [LOCKING]
- [verbs]
- [attributes]
- [@asuccess]
- [@asuccess]
- [@asuccess]
:::

# Switches

  Commands can have "switches" which modify the behavior of the command. Switches are attached after the end of a command. For example, most people are familiar with the command

    `@lock` me==me

  The "enter" switch to `@lock` allows you to lock who can enter:

    `@lock`/enter me==me

  A command may have multiple switches:

    `@pemit`/noeval/silent me=Hi!

  Help on the switches available for a command is available in the help file for that command.<br>
  (If you are looking for information on `@switch`, see [`@switch`].)

# Types of Objects
# Types
# Objects
  Everything on a MUSH is an object in the MUSH database. There are four main types of objects: players, things (once called 'objects'), rooms and exits. You can see the type of an object when you 'examine' it, or with the `type()` function.

  There is also a 'garbage' type, used for objects which have been created and then `@destroyed`. Garbage objects cannot be used in any way, and their dbrefs will be recycled (with a new objid) when something new is created.

  For more information on any of the types, see [`<type>`].

  The `@stats` command lists how many objects of each type currently exits in the database.


::: seealso
- [TYPE()]
- [HASTYPE()]
:::

# Players
  Players can be created at the login screen (or with `@pcreate`) and connected to. They can also receive `@mail`, and have a number of attributes set on them by the MUSH automatically, including LAST, LASTSITE, etc. They can have multiple `@aliases`, and are checked for `$-commands` and ^-listens.

  `@linking` a player sets their home.


::: seealso
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@pcreate]
:::

# Rooms
  Rooms are outermost-containers: they cannot be moved. They are created with `@dig`. Rooms can contain exits. They are checked for `$-commands` and ^-listens. Rooms do not have aliases.

  `@linking` a room creates a drop-to. Rooms have no location; `loc()` on a room returns its drop-to (as does `home()`).


::: seealso
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@dig]
- [DROP-TOS]
:::

# Things
  Things are created with `@create`. They can move around the MUSH, be entered and carried. They are checked for `$-commands` and ^-listens. Things do not have aliases.

  `@linking` a thing sets its home.


::: seealso
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@create]
:::

# Exits
  Exits are created with `@open`. They link rooms together, and can be used by players and things to move from one room to another with the GOTO command. Exits cannot move (though they can be `@teleported` to another room). They can have multiple aliases in their `@alias`. They are NOT checked for `$-commands` or ^-listens. Exits can have variable destinations; see [`@destination`] for more information.

  You can change the destination of an exit with the `@link` command. `home()` returns the source room of an exit, and `loc()` returns its destination.

  Setting an exit CLOUDY and/or TRANSPARENT causes its destination's description and/or contents to be shown after the exit's description when looked at.

  Sound is propagated through exits which are set AUDIBLE, as long as their source room (home) is also set AUDIBLE.


::: seealso
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@open]
- [@link]
- [@destination]
- [AUDIBLE]
:::

# Garbage
  Garbage objects previously existed as one of the four main types (player, thing, exit or room) but were destroyed with `@destroy`/`@nuke`. They exist only as placeholders in the database; you can do nothing with them. The dbrefs of garbage objects will be reused when new objects are created (but with a different creation time/object id).

  The total number of garbage objects, and the next garbage object to be recycled, is shown in `@stats`. You can use lsearch(all,type,garbage) to get a list of all garbage dbrefs.


::: seealso
- [@destroy]
- [@stats]
:::

# Warnings

  If the MUSH is configured to do so, players may receive regular warnings about potential building problems on objects that they own. The interval is set in the 'warn_interval' `@config` option; if set to 0, automatic warnings are disabled. You can also check warnings, either for a specific object or all objects you own, with `@wcheck`.

  For more information, see the following help topics:<br>
    `@warnings`        `@wcheck`         NO_WARN         WARNINGS LIST

# Wildcards
# *
# ?
# **
  SharpMUSH has two standard wildcards, which can be used in `$-commands`, as well as a number of softcode functions: an asterisk (*) matches zero or more of any characters, and a question mark (?) matches exactly one character. The most common use of wildcards is to allow people to pass arguments to `$-commands`. For example, let's say you want to have a 'wave' command which allows you to wave to a specific person:

```sharp
    > &cmd.wave me=$wave *: pose waves.
```

  The "*" wildcard will match anything, allowing you to type "wave foo", "wave bar", etc. You can use the %0-%9 substitutions to get the values which matched the first ten wildcards in the command:

```sharp
    > &cmd.wave me=$wave *: pose waves to %0.
    > wave to Muse
    Mike waves to Muse.
```

  A backslash (\) can be used to escape * and ? if you want to match a literal asterisk or question mark. (Note that you will often have to use \\ so that the softcode parser doesn't evaluate it away!)

```sharp
  > think strmatch(foobar, ?*?)
  1
  > think strmatch(foobar, \\?*\\?)
  0
  > think strmatch(?foobar?, \\?*\\?)
  1
```

  The "**" wildcard is also available for matching attribute names - see HELP attribute trees browsing for more information.

  It's also possible to use regular expressions, rather than wildcards, for matching strings. Regexps allow a lot more control over what is matched, but are therefore somewhat more complex. See [regexp] for details.


::: seealso
- [$-commands]
- [regexp]
- [STACK]
- [registers]
:::

# Zone Master Rooms
# ZMRs

  Zone Master Rooms are a subset of zones. If a room is used as a zone, it is called a Zone Master Room (ZMR). ZMRs are like local "master" rooms; exits in the ZMR are global to that zone, and `$-commands` on objects in the ZMR are global to that zone (`$-commands` on the ZMR itself, like `$-commands` on the master room, are ignored). If a ZMR is a player's personal zone, objects in the ZMR are checked for commands that the player can use anywhere (but exits are not checked unless the player is in a zoned room).

  Zone Master Rooms are useful either if you need global exits for the zone, or if the zone has a lot of `$-commands` which need to be restricted to different groups, as they can go on separate use-locked objects.


::: seealso
- [zones]
- [MASTER ROOM]
- [evaluation order]
:::

# Matching
  Matching is the process the MUSH uses to determine which object you mean when you try to do something with an object. Different commands and functions do matching in different ways, but most will allow you to specify an object as:
    * its dbref (#7) or objid (#7:123456789)
    * the string "me" (yourself)
    * the string "here" (the room you're in)
    * a '*' followed by a playername (*javelin)
    * its full name (Box of chocolates)
    * part of any word in its name, if nothing else shares that part (Box)

  Using the object's name only works if the object is near you.

  You can usually qualify an object with an adjective (English matching) to help the MUSH determine which object you mean Adjectives include:
    * my `<obj>` - an object you're carrying
    * this `<obj>` - an object in your location (also: this here `<obj>`)
    * toward `<exit>` - an exit in your location
    * 1st, 2nd, etc. `<obj>` - one of a set of objects with the same names. Objects are ordered in the order in which they're listed in your inventory, room contents, and exit list (in that order). If there aren't enough objects, this will fail.	You can use an adjective with an ordinal (my 1st `<obj>`, this 2nd `<obj>`, etc)

  In commands that take a list of space-separated names (like page), you'll need to enclose names with spaces in "double quotes". The same is true on the login screen. For example:<br>
    page "Leeroy Jenkins"=Stop doing that.

# &HELP
This is the AHELP index.

# Descriptor
# Port
  A descriptor (also called a port or socket descriptor) is a unique (though reusable) number assigned to each connection to the MUSH. The descriptor for each connection is shown on the Wizard WHO in the 'Des' column.

  Several commands and functions take a descriptor as an argument, or return the descriptor(s) associated with a player's connection.

::: seealso
- [who]
- [%]
- [LPORTS()]
- [LPORTS()]
- [PLAYER()]
- [@boot]
- [Connection functions]
:::

# Unicode

At the moment, SharpMUSH has very minimal support for Unicode. Almost all text is treated internally as being in the Latin-1 character set. Clients that support telnet character set negotiation can send and receive UTF-8, but only characters in the Basic Latin and Latin-1 Supplement blocks are accepted (Others are replaced with question marks).

A few functions support Unicode-aware text transformations:

- [STRIPACCENTS()]

When the MUSH is compiled with the ICU library (See `@config` compile), additional functions support Unicode-aware text transformations:

- [LCSTR()]
- [UCSTR()]

When using these functions, do not assume that they return the same number of characters as their argument.

