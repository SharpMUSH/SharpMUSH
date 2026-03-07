# PennMUSH vs SharpMUSH: Output Comparison

## Overview

This document compares the output and behavior of PennMUSH (latest from GitHub, built from
source on Ubuntu 24.04, March 2026) against SharpMUSH's current implementation. PennMUSH was
run locally, connected via telnet on port 4201, and commands were exercised programmatically.

**PennMUSH version tested:** 1.8.x (cloned from https://github.com/pennmush/pennmush,
built with `./configure --disable-sql && make && make install`)

---

## 1. Connection & Welcome

### PennMUSH
On TCP connect, PennMUSH immediately sends telnet negotiation bytes followed by the contents
of `game/txt/connect.txt`:
```
<This is where you announce that they've connected to your MUSH>
...
Use connect <name> <password> to connect to your existing character.
Use create <name> <password> to create a character.
Use QUIT to logout.
```

After `connect One` (god character, no password on minimal DB):
```
Welcome to PennMUSH!
--------------------------------------------------------------------------
Yell at your god to personalize this file
...
MAIL: You have no mail.

Room Zero(#0RL)
You are in Room Zero.
```

### SharpMUSH
Sends welcome file on connect, then after `connect` the player receives a LOOK at their
current location. The format is consistent.

**Difference:** Both support the same `connect <name> <password>` syntax. Welcome text is
configurable in both. SharpMUSH uses a `connect.txt` equivalent.

---

## 2. WHO Command

### PennMUSH
```
Player Name       Loc #    On For  Idle  Cmds Des  Host
One                  #0        1s    0s     2  16 localhost
There is one player connected.
```

**Columns:** Player Name (18), Location # (8), Connection Time (7), Idle Time (4),
Commands (4), Description Length (3), Hostname

### SharpMUSH
```
Player Name         On For  Idle  Doing
One                     1s    0s  
1 players logged in.
```

**Columns:** Player Name (18), Connection Time (6), Idle Time (4), Doing text (32)

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Location column | `Loc #` (shows dbref) | **Missing** |
| Commands column | `Cmds` (count since login) | **Missing** |
| Description column | `Des` (DOING length) | Shows full DOING text |
| Hostname column | `Host` | **Missing** |
| Footer | `There is one player connected.` | `1 players logged in.` |

---

## 3. LOOK Command

### PennMUSH
```
Room Zero(#0RL)
A simple test room with some exits.
Obvious exits:
East, South, and North
```

Room name header always shown as `Name(#dbrefFlags)` format.
Exits use Oxford comma: `East, South, and North`

For an object:
```
Widget(#6Tn)
A small metallic widget.
```

### SharpMUSH
```
Room Zero(#0RL)
A simple test room with some exits.
Obvious exits:
North, South, East
```

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Exit list delimiter | Serial comma with "and" before last: `East, South, and North` | Comma only: `North, South, East` |
| Room name style | `Name(#dbrefFlags)` | Same format |
| Name color | Plain | White ANSI color applied |

---

## 4. INVENTORY Command

### PennMUSH
```
You are carrying:
TestCreation(#9Tn)
Widget(#6Tn)
TestObject(#3Tn)
You have unlimited Pennies.
```

### SharpMUSH
```
You are carrying:
Widget(#3Tn)
```
(Penny display depends on configuration; may differ)

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Penny display | Shows `You have unlimited Pennies.` (god) | May omit penny line |
| Object format | `Name(#dbrefFlags)` per line | Same |

---

## 5. SAY Command

### PennMUSH
- **To speaker:** `You say, "Hello there!"`
- **To others in room:** `One says, "Hello there!"`

### SharpMUSH (Current Behavior)
The SAY command passes the **raw message** to `NotifyService.Notify()` with
`NotificationType.Say` type. No "says," prefix formatting is applied before the notification.

The expected behavior (per skipped unit tests) is:
- `One says, "Hello world"` *(same for everyone including speaker)*

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Speaker's view | `You say, "message"` | Raw message (no "says," prefix) |
| Others' view | `Name says, "message"` | Raw message |
| Second-person for speaker | Yes (`You say`) | No (no formatting at all) |

**Note:** This is a known incomplete feature in SharpMUSH. The `SocialCommandTests` are
marked `[Skip("Issue with NotifyService mock, needs investigation")]`.

---

## 6. POSE Command

### PennMUSH
- `pose waves cheerfully.` → **All see:** `One waves cheerfully.`
- `:grins broadly.` → **All see:** `One grins broadly.`
- `;'s smile brightens.` → **All see:** `One's smile brightens.` (no space)

### SharpMUSH
The POSE command passes the raw message with `NotificationType.Pose`.
Similar structural gap to SAY — name prepending may not be implemented.

---

## 7. @EMIT Command

### PennMUSH
```
@emit Something happens here.
```
**Output:** `Something happens here.` (bare — no prefix)

### SharpMUSH
Same behavior — `@emit` sends the evaluated text directly to the room.

**Difference:** None significant.

---

## 8. @CREATE Command

### PennMUSH
```
@create TestObject
```
**Output:** `Created: Object #3.`

### SharpMUSH
```
Created TestObject (#3).
```

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Format | `Created: Object #N.` | `Created {Name} (#N).` |
| Includes name | No (just "Object") | Yes (shows actual name) |

---

## 9. @DIG Command

### PennMUSH
```
@dig TestRoom
```
**Output:**
```
TestRoom created with room number 4.
```

### SharpMUSH
```
TestRoom created with room number #4.
```

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Room number format | `...number 4.` (no #) | `...number #4.` (with #) |

---

## 10. @OPEN Command

### PennMUSH
```
@open North;n=#1
```
**Output:**
```
Opened exit #5
Trying to link...
Linked exit #5 to #1
```

### SharpMUSH
```
Opened exit North with dbref #5.
Trying to link...
Linked exit #5 to #1
```

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Opened message | `Opened exit #N` | `Opened exit {Name} with dbref #N.` |
| Trying/Linked | Same | Same |

---

## 11. @SET Flag Command

### PennMUSH
```
@set me=DARK
```
**Output:** `One - DARK set.`

```
@set me=!DARK
```
**Output:** `One - DARK reset.`

### SharpMUSH
```
@set me=DARK
```
**Output:** `Flag: DARK Set.`

```
@set me=!DARK
```
**Output:** `Flag: DARK Unset.`

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Set format | `{Name} - {FLAG} set.` | `Flag: {FlagName} Set.` |
| Unset word | "reset" | "Unset" |
| Includes object name | Yes | No |

---

## 12. Attribute Setting (&ATTR)

### PennMUSH
```
&MYATTR me=Testing value 123
```
**Output:** `One/MYATTR - Set.`

```
@set me=ATTRNAME:value
```
**Output:** `One/ATTRNAME - Set.`

```
@set me/ATTRNAME=somevalue
```
**Output:** `Unrecognized attribute flag.` *(treats `somevalue` as a flag name on the attribute)*

### SharpMUSH
All three syntaxes behave the same as PennMUSH:
- `&ATTR obj=value` → `{Name}/{ATTR} - Set.`
- `@set obj=ATTR:value` → `{Name}/{ATTR} - Set.`
- `@set obj/ATTR=flag` → Sets attribute flag (not value)

**Difference:** None — SharpMUSH matches PennMUSH behavior for attribute setting.

---

## 13. EXAMINE Command

### PennMUSH
```
examine me
```
**Output:**
```
One(#1PUWc)
Type: PLAYER Flags: UNFINDABLE WIZARD CONNECTED
A test player.
Owner: One(#1PUWc)  Zone: *NOTHING*  Pennies: 150
Parent: *NOTHING*
Basic Lock [#1i]: =One(#1PUWc)
Enter Lock [#1i]: =One(#1PUWc)
Use Lock [#1i]: =One(#1PUWc)
Powers: 
Channels: *NONE*
Warnings checked: 
Created: Thu Mar 05 07:24:53 2026
LAST [#1cwv+]: Thu Mar 05 07:25:43 2026
...
Carrying:
TestObject(#3Tn)
Home: Room Zero(#0RL)
Location: Room Zero(#0RL)
```

### SharpMUSH
```
examine me
```
**Output format is similar but differs in:**
- Attribute display order and format
- Timestamps use different format (ISO vs Unix-like)
- Flag abbreviations may differ

### Differences
| Aspect | PennMUSH | SharpMUSH |
|--------|----------|-----------|
| Timestamp format | `Thu Mar 05 07:24:53 2026` | ISO format / millisecond-based |
| System attrs shown | LAST, LASTFAILED, LASTIP, LASTSITE, MAILCURF, MAILFOLDERS, RQUOTA | Similar set |
| Lock display | `Basic Lock [#dbrefFlags]: expression` | Slightly different |

---

## 14. @STATS Command

### PennMUSH
```
@stats
```
**Output:** `7 objects = 3 rooms, 1 exits, 2 things, 1 players, 0 garbage.`

```
@stats me
```
**Output:** `7 objects = 3 rooms, 1 exits, 2 things, 1 players.` *(no garbage count for players)*

### SharpMUSH
Format should be similar (needs verification).

---

## 15. PAGE Command

### PennMUSH
- `page #1=Hello` → `You paged One with 'Hello.'` to sender, `One pages: Hello` to recipient
- `page me=Hello` → `I can't find who you're trying to page with: me` + `Unable to page: me`
  (PennMUSH requires numeric dbref or full name; "me" not recognized in page arguments)

### SharpMUSH
- `page #1=Hello` → Various formats depending on implementation
- The PAGE command uses `NotificationType.Say` internally

---

## 16. WHISPER Command

### PennMUSH
```
whisper me=Secret message
```
**Output to whisperer:** `You whisper, "Secret message" to One.`
**Output to recipient:** `One whispers: Secret message`

### SharpMUSH
- Whisper uses SAY notification type with partial implementation

---

## 17. Error Messages

### PennMUSH
| Situation | Message |
|-----------|---------|
| Unknown command | `Huh?  (Type "help" for help.)` |
| Invalid dbref | `I can't see that here.` (player message) + `#-1 NO SUCH OBJECT VISIBLE` (function return) |
| Division by zero | `#-1 DIVISION BY ZERO` |
| Non-numeric args | `#-1 ARGUMENTS MUST BE NUMBERS` |
| Invalid @tel target | `No match.` |
| Look invalid object | `I don't see that here.` |

### SharpMUSH
| Situation | Message |
|-----------|---------|
| Unknown command | `Huh?  (Type "help" for help.)` ✓ |
| Invalid dbref (function) | `#-1 NO SUCH OBJECT VISIBLE` ✓ |
| Division by zero | `#-1 DIVISION BY ZERO` ✓ |
| Non-numeric args | `#-1 ARGUMENTS MUST BE NUMBERS` ✓ |

**Note:** PennMUSH sends TWO messages for invalid object refs: a player-visible message
(`I can't see that here.`) AND the function error return (`#-1 NO SUCH OBJECT VISIBLE`).
SharpMUSH may only send the error return.

---

## 18. Function Comparison

### Function Inventory (Verified via `@list/functions`)

The authoritative function list was obtained by running `@list/functions` as wizard in PennMUSH
and grepping `[SharpFunction]` attribute declarations in the SharpMUSH source.

- **PennMUSH:** 527 functions
- **SharpMUSH:** 540 functions

### Functions in PennMUSH NOT in SharpMUSH (5)

These PennMUSH functions are currently missing from SharpMUSH:

| Function | Notes |
|----------|-------|
| `convutcsecs()` | Convert UTC time string to epoch seconds |
| `convutctime()` | Convert epoch seconds to UTC time string |
| `ncand()` | Negated conditional AND (SharpMUSH uses `cnand()` instead) |
| `reglmatchalli()` | Case-insensitive regex list match all (SharpMUSH uses `regmatchalli()`) |
| `xmwho()` | Extended who list for zones |

**Note:** `ncand()` (PennMUSH) and `cnand()` (SharpMUSH) are confirmed to be the same function
— both implement short-circuit conditional NAND (equivalent to `not(cand())`). PennMUSH's help
for `ncand` says it's the conditional (short-circuit) version; SharpMUSH names the same
operation `cnand`. Verified truth table: both return `1` for any false input, `0` only when all true.

`reglmatch*` (PennMUSH "list regex" family) and `regmatchalli` (SharpMUSH) are **different
functions** — they are not simply renamed versions of each other. PennMUSH's `regl*` family
operates on a list of words (each word tested against the pattern), while SharpMUSH's
`regmatchalli` matches substrings within a single string. See the behavioral difference below.

### SharpMUSH-Only Functions (18, not in PennMUSH)

These exist in SharpMUSH as extensions:

| Function | Purpose |
|----------|---------|
| `attrib_set#()` | Internal attribute set variant |
| `cinfo()` | Channel info |
| `cnand()` | Negated conditional AND (PennMUSH uses `ncand()`) |
| `decomposeweb()` | Web-specific decompose |
| `delete()` | String/list delete (PennMUSH uses `strdelete()`/`ldelete()`) |
| `downmotd()` | Shutdown MOTD text |
| `fullmotd()` | Full MOTD text |
| `idlesecs()` | Idle seconds (PennMUSH uses `idle()`) |
| `insert()` | List insert (PennMUSH uses `linsert()`/`strinsert()`) |
| `motd()` | MOTD text |
| `regmatchalli()` | Case-insensitive regex match all (PennMUSH uses `reglmatchalli()`) |
| `regreplace()` | Regex string replace (PennMUSH uses `regedit()`) |
| `rendermarkdown()` | Render Markdown to MUSH output |
| `rendermarkdowncustom()` | Render Markdown with custom settings |
| `websocket_html()` | WebSocket HTML output |
| `websocket_json()` | WebSocket JSON output |
| `wizmotd()` | Wizard-only MOTD text |
| `zfind()` | Zone find |

### Common Function Names NOT in Either System

The following function names do **not** exist in either PennMUSH or SharpMUSH. Code using
these names will get `#-1 FUNCTION (X) NOT FOUND` in both systems:

| Name | Correct Function to Use |
|------|------------------------|
| `dbref()` | Use `num()` |
| `concat()` | Use `cat()` |
| `upper()` | Use `ucstr()` |
| `lower()` | Use `lcstr()` |
| `ltrim()` | Use `trim(l,...)` |
| `rtrim()` | Use `trim(r,...)` |
| `mod()` | Use `remainder()` or `modulo()` |
| `word()` | Use `extract()` |
| `subst()` | Use `edit()` |
| `count()` | Use `words()` |
| `attrcnt()` | Use `words(lattr(obj))` |
| `flag()` | Use `flags()` |

### Function Behavioral Differences

#### `lnum()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `lnum(5)` | `0 1 2 3 4` | `0 1 2 3 4` ✓ |
| `lnum(1,5)` | `1 2 3 4 5` | `1 2 3 4 5` ✓ |
| `lnum(0,10,2)` | `0 2 4 6 8 10` | `0 2 4 6 8 10` ✓ |

#### `splice()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `splice(a b c,b,X)` | `#-1 NUMBER OF WORDS MUST BE EQUAL` | Different signature/behavior |

PennMUSH `splice(list1, list2, pos)` requires same-length lists; it interleaves them.
SharpMUSH `splice(list1, list2, position, delimiter)` - different behavior.

#### `wordpos()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `wordpos(hello world foo, world)` | `#-1 ARGUMENT MUST BE INTEGER` | May differ |

In PennMUSH, `wordpos(list, position)` takes a **numeric** position (returns the word at that
position). SharpMUSH may have different argument interpretation.

#### `replace()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `replace(hello world,world,earth)` | `hello world` (no change, "world" not valid position) | Likely different — SharpMUSH has `lreplace()` |

In PennMUSH, `replace(list, position, word)` replaces word at **numeric** position.
Passing `world` as the position returns unchanged list.

#### `pos()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `pos(ell,hello)` | `2` (1-indexed) | `2` ✓ |

#### `mid()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `mid(hello,1,3)` | `ell` (0-indexed start) | `ell` ✓ |

#### `flags()`
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `flags(me)` | `PUWc` (abbreviated) | Abbreviated flags |

#### `num()` — the canonical dbref function
| Call | PennMUSH | SharpMUSH |
|------|----------|-----------|
| `num(me)` | `#1` | `#1` ✓ |
| `dbref(me)` | `#-1 FUNCTION (DBREF) NOT FOUND` | `#-1 FUNCTION (DBREF) NOT FOUND` |

Neither PennMUSH nor SharpMUSH has `dbref()`. The correct function is `num()`.

### Functions in Both Systems (Matching Behavior)
| Function | PennMUSH | SharpMUSH |
|----------|----------|-----------|
| `add(2,3)` | `5` | `5` ✓ |
| `sub(10,3)` | `7` | `7` ✓ |
| `mul(4,5)` | `20` | `20` ✓ |
| `div(10,3)` | `3` (integer) | `3` ✓ |
| `fdiv(10,3)` | `3.333333` | `3.333333` ✓ |
| `abs(-5)` | `5` | `5` ✓ |
| `max(1,2,3)` | `3` | `3` ✓ |
| `min(1,2,3)` | `1` | `1` ✓ |
| `sqrt(16)` | `4` | `4` ✓ |
| `floor(3.7)` | `3` | `3` ✓ |
| `ceil(3.2)` | `4` | `4` ✓ |
| `round(3.567,2)` | `3.57` | `3.57` ✓ |
| `strlen(hello)` | `5` | `5` ✓ |
| `mid(hello,1,3)` | `ell` | `ell` ✓ |
| `left(hello,3)` | `hel` | `hel` ✓ |
| `right(hello,3)` | `llo` | `llo` ✓ |
| `ucstr(hello)` | `HELLO` | `HELLO` ✓ |
| `lcstr(HELLO)` | `hello` | `hello` ✓ |
| `capstr(hello world)` | `Hello world` | `Hello world` ✓ |
| `trim(  hello  )` | `hello` | `hello` ✓ |
| `repeat(ab,3)` | `ababab` | `ababab` ✓ |
| `reverse(hello)` | `olleh` | `olleh` ✓ |
| `cat(hello,world)` | `hello world` | `hello world` ✓ |
| `words(hello world foo)` | `3` | `3` ✓ |
| `extract(hello world foo,1,2)` | `hello world` | `hello world` ✓ |
| `before(hello world,world)` | `hello` | `hello` ✓ |
| `after(hello world,hello)` | ` world` | ` world` ✓ |
| `tr(hello,eo,EO)` | `hEllO` | `hEllO` ✓ |
| `pos(ell,hello)` | `2` | `2` ✓ |
| `lnum(5)` | `0 1 2 3 4` | `0 1 2 3 4` ✓ |
| `lnum(1,5)` | `1 2 3 4 5` | `1 2 3 4 5` ✓ |
| `member(a b c,b)` | `2` | `2` ✓ |
| `sort(3 1 2)` | `1 2 3` | `1 2 3` ✓ |
| `iter(1 2 3,##)` | `1 2 3` | `1 2 3` ✓ |
| `setunion(a b c,b c d)` | `a b c d` | `a b c d` ✓ |
| `setinter(a b c,b c d)` | `b c` | `b c` ✓ |
| `setdiff(a b c,b c d)` | `a` | `a` ✓ |
| `itemize(one two three)` | `one, two, and three` | `one, two, and three` ✓ |
| `if(1,yes,no)` | `yes` | `yes` ✓ |
| `if(0,yes,no)` | `no` | `no` ✓ |
| `ifelse(1,yes,no)` | `yes` | `yes` ✓ |
| `and(1,1)` | `1` | `1` ✓ |
| `or(0,1)` | `1` | `1` ✓ |
| `not(0)` | `1` | `1` ✓ |
| `t(1)` | `1` | `1` ✓ |
| `t(0)` | `0` | `0` ✓ |
| `switch(2,1,one,2,two,other)` | `two` | `two` ✓ |
| `case(2,1,one,2,two,other)` | `two` | `two` ✓ |
| `type(me)` | `PLAYER` | `PLAYER` ✓ |
| `type(here)` | `ROOM` | `ROOM` ✓ |
| `loc(me)` | `#0` | `#0` ✓ |
| `home(me)` | `#0` | `#0` ✓ |
| `owner(me)` | `#1` | `#1` ✓ |
| `hasflag(me,wizard)` | `1` | `1` ✓ |
| `lattr(me)` | `DESCRIBE LAST...` | Similar list ✓ |
| `default(me/NONEXIST,default)` | `default` | `default` ✓ |
| `edefault(me/NONEXIST,[add(1,2)])` | `3` | `3` ✓ |
| `xget(me,ATTRNAME)` | value | value ✓ |
| `hasattr(me,ATTRNAME)` | `1`/`0` | `1`/`0` ✓ |
| `name(me)` | `One` | Player's name ✓ |
| `num(me)` | `#1` | `#1` ✓ |
| `add(hello,world)` | `#-1 ARGUMENTS MUST BE NUMBERS` | Same ✓ |
| `div(1,0)` | `#-1 DIVISION BY ZERO` | Same ✓ |

---

## 19. Channels

### PennMUSH
PennMUSH uses `@channel` commands:
- `@channel/add Public=p` — creates channel (note: `@ccreate` returns `Huh?`)
- `+channel message` — not standard PennMUSH syntax (requires custom softcode)
- PennMUSH channel commands: `@channel/add`, `@channel/join`, `@channel/list`

### SharpMUSH
Uses `@ccreate`, `+channel message` and channel commands via `@channel`.

**Difference:** SharpMUSH uses different channel command names than PennMUSH out-of-the-box.

---

## 20. THINK Command

### PennMUSH
- `think [expression]` → evaluates expression, outputs result to player only
- `think Hello World` → `Hello World`
- `version` → `Huh?  (Type "help" for help.)` (not a valid command in PennMUSH)

### SharpMUSH
- Same: `think [expression]` evaluates and outputs to player

---

## Summary of Key Differences

### High Priority (Functional Gaps)

1. **SAY command** — SharpMUSH does not format say messages with "Name says, ..." prefix.
   PennMUSH: speaker sees `You say, "msg"`, others see `Name says, "msg"`.
   SharpMUSH currently sends raw message to all.

2. **WHO columns** — SharpMUSH missing: Location #, Commands count, Hostname columns.

3. **@SET flag output format** — Different formats:
   - PennMUSH: `{ObjectName} - {FLAG} set.` / `{ObjectName} - {FLAG} reset.`
   - SharpMUSH: `Flag: {FlagName} Set.` / `Flag: {FlagName} Unset.`

4. **LOOK exit list** — PennMUSH uses Oxford comma with "and": `East, South, and North`
   SharpMUSH uses plain comma: `East, South, North`

### Medium Priority (Format Differences)

5. **@CREATE output** — PennMUSH: `Created: Object #N.` / SharpMUSH: `Created {Name} (#N).`

6. **@DIG output** — PennMUSH: `...number 4.` / SharpMUSH: `...number #4.` (extra `#`)

7. **@OPEN output** — PennMUSH: `Opened exit #N` / SharpMUSH: `Opened exit {Name} with dbref #N.`

8. **Error messages for invalid objects** — PennMUSH sends TWO messages: a player notification
   (`I can't see that here.`) plus the function error return. SharpMUSH behavior may differ.

### Low Priority (Minor/Extension Differences)

9. **`ncand()` vs `cnand()`** — PennMUSH uses `ncand()`, SharpMUSH uses `cnand()` for the
   same short-circuit conditional NAND operation (confirmed via truth table). Naming inconsistency only.

10. **`reglmatch*` vs `regmatch*` family** — PennMUSH's `reglmatch*` functions operate on a
    **list** of words (each word tested against pattern). SharpMUSH's `regmatchalli` operates on a
    single string finding all substring matches. These are different functions, not just renamed.

11. **SharpMUSH-only functions** — `rendermarkdown()`, `websocket_html()`, `websocket_json()`,
    `regreplace()`, `zfind()` and MOTD functions (`motd()`, `wizmotd()`, `downmotd()`, `fullmotd()`)
    exist in SharpMUSH but not PennMUSH — these are SharpMUSH extensions.

12. **`@DIG` room number with #** — Minor cosmetic difference.

13. **WHO footer** — PennMUSH: `There is one player connected.` / SharpMUSH: `1 players logged in.`

---

## PennMUSH Build Notes

```bash
# Clone PennMUSH
git clone --depth=1 https://github.com/pennmush/pennmush.git /tmp/pennmush

# Build (Ubuntu 24.04, needs: gcc, make, perl, libssl-dev, libpcre2-dev)
cd /tmp/pennmush
./configure --disable-sql
make -j4
make install

# Start server (minimal DB auto-created on first run)
cd game
./netmush mush.cnf &

# Connect via telnet
telnet localhost 4201

# Login as god (no password on minimal DB)
connect One
```

Default port: 4201. God character: `One` (no password on fresh minimal DB).
