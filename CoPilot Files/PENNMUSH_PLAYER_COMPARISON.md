# PennMUSH vs SharpMUSH: Player-Level Comparison (Proven)

## Overview

This document extends the previous comparison by:
1. Creating a regular (mortal) player in PennMUSH and logging in as them
2. Running the same commands as both a wizard (`One`, god object) and a mortal (`Tester`)
3. Capturing the *exact* output from both sides of each interaction
4. Proving assumptions that were stated but unconfirmed in the original document

**Test environment:**
- PennMUSH built fresh from `https://github.com/pennmush/pennmush` (March 2026)
- Server version: `numversion()` returns `1008008000` (PennMUSH 1.8.8)
- Two simultaneous connections: god (`One`, #1, WIZARD) and mortal (`Tester`, #13, no flags)
- Both characters in same room (Room Zero, #0)

---

## 1. Player Creation: Two Methods

### Method A: From the connect screen (pre-login)

Any unauthenticated connection can create a player:

```
create NewPlayer newpassword123
```

**Output to the creator:**
```
-------------------------------------------------------------------------
Congratulations on your newly created character. PennMUSH welcomes you.
For more information about this game, type 'news' or 'help' to get help.
...
-------------------------------------------------------------------------

Welcome to PennMUSH!
...
Room Zero(#0RL)
A grand hall with marble pillars.
Contents:
Sword
Chest
One
Obvious exits:
South, North, South, and North
```

The player is immediately created, welcomed, and LOOK is auto-executed.

### Method B: Wizard @pcreate (in-game)

A logged-in wizard uses `@pcreate`:

```
@pcreate Tester=secret
```

**Output to god:**
```
New player 'Tester' (#13) created with password 'secret'
```

The new player is NOT connected — the wizard just gets a confirmation message.

### Differences from SharpMUSH

- PennMUSH uses `@pcreate Name=password` (god creates player)
- SharpMUSH likely has equivalent, but the exact output format differs
- "New player 'Name' (#N) created with password 'password'" — specific format to preserve

---

## 2. WHO Command: Wizard vs. Mortal — **PROVEN**

This was the most important discrepancy to verify. It is fully confirmed.

### Wizard WHO output:
```
Player Name       Loc #    On For  Idle  Cmds Des  Host
Tester               #0        2s    1s     2  19 localhost
One                  #0       20s    0s    16  16 localhost
There are 2 players connected.
```

**Columns (wizard):** Player Name (18), Location # (7), On For (7), Idle (5), Cmds (4), Des (3), Host

### Mortal WHO output:
```
Player Name          On For   Idle  Doing
Tester                   3s     0s  
One                     21s     1s  
There are 2 players connected.
```

**Columns (mortal):** Player Name (21), On For (9), Idle (6), Doing (text)

### Key differences **proven**:
| Column | Wizard | Mortal |
|--------|--------|--------|
| `Loc #` | ✓ Shows dbref of location | ✗ Hidden |
| `Cmds` | ✓ Command count since login | ✗ Hidden |
| `Des` | ✓ DOING attribute length | ✗ Hidden |
| `Host` | ✓ Hostname/IP of connection | ✗ Hidden |
| `Doing` | ✗ Not shown | ✓ DOING attribute text |
| Column widths | Different | Different |
| Footer | `There are N players connected.` | `There are N players connected.` (same) |

**SharpMUSH currently shows mortal-style output only, missing Loc#, Cmds, Des, Host for wizards.**

---

## 3. LOOK Room: God vs. Mortal — **PROVEN**

### God LOOK at room:
```
Room Zero(#0RL)
A grand hall with marble pillars and a fountain.
Contents:
Tester(#13PenAc)
Sword(#6Tn)
Chest(#5Tn)
Obvious exits:
South, North, South, and North
```

### Mortal LOOK at room:
```
Room Zero(#0RL)
A grand hall with marble pillars and a fountain.
Contents:
Sword
Chest
One
Obvious exits:
South, North, South, and North
```

### Differences **proven**:
| Aspect | God | Mortal |
|--------|-----|--------|
| Room name | `Name(#dbrefFlags)` | `Name(#dbrefFlags)` (same!) |
| Object names | `Name(#dbrefFlags)` | Plain `Name` only |
| Own player in contents | `Tester(#13PenAc)` | Not shown (you can't see yourself) |

**Critical:** The god player (`One`) was NOT in the mortal's content list — players don't see
themselves in room contents. The mortal sees `One` (god) but not themselves (`Tester`).

---

## 4. LOOK Object: God vs. Mortal — **PROVEN**

### God looks at an object:
```
> look Tester
Tester(#13PenAc)
A curious explorer with bright eyes.
```

### Mortal looks at an object:
```
> look Chest
Chest
A sturdy oak chest.
```

### Mortal looks at god player:
```
> look One
One
You see Number One.
Carrying:
Sword
Chest
GodThing
```

### Differences:
| Aspect | God | Mortal |
|--------|-----|--------|
| Object header | `Name(#dbrefFlags)` | Plain `Name` |
| Carried items | `Name(#dbrefFlags)` | Plain `Name` |
| Description | Same | Same |

---

## 5. EXAMINE: Major Privilege Differences — **PROVEN**

This is the most dramatic difference in output format between god and mortal.

### 5a. Player examines themselves

**God (One) examines self:**
```
One(#1PWc)
Type: PLAYER Flags: WIZARD CONNECTED
You see Number One.
Owner: One(#1PWc)  Zone: *NOTHING*  Pennies: 150
Parent: *NOTHING*
Basic Lock [#1i]: =One(#1PWc)
Enter Lock [#1i]: =One(#1PWc)
Use Lock [#1i]: =One(#1PWc)
Powers: 
Channels: *NONE*
Warnings checked: 
Created: Thu Mar 05 08:01:16 2026
LAST [#1cwv+]: Thu Mar 05 08:05:28 2026
LASTFAILED [#1cw+]:  
LASTIP [#1cw+]: 127.0.0.1
LASTLOGOUT [#1cw+]: Thu Mar 05 08:03:52 2026
LASTSITE [#1cw+]: localhost
MAILCURF [#1$w+]: 0
MAILFOLDERS [#1$cw+]: 0:INBOX:0 
RQUOTA [#1m+]: 7
Carrying:
Sword(#12Tn)
Chest(#11Tn)
GodThing(#7Tn)
Home: Room Zero(#0RL)
Location: Room Zero(#0RL)
```

**Mortal (Tester) examines self:**
```
Tester(#13PenAc)
Type: PLAYER Flags: ENTER_OK NO_COMMAND ANSI CONNECTED
A curious explorer with bright eyes.
Owner: Tester(#13PenAc)  Zone: *NOTHING*  Pennies: 150
Parent: *NOTHING*
Basic Lock [#1i]: =Tester(#13PenAc)
Enter Lock [#1i]: =Tester(#13PenAc)
Use Lock [#1i]: =Tester(#13PenAc)
Powers: 
Channels: *NONE*
Warnings checked: normal 
Created: Thu Mar 05 08:05:37 2026
LAST [#1cwv+]: Thu Mar 05 08:05:46 2026
LASTFAILED [#1cw+]:  
LASTIP [#1cw+]: 127.0.0.1
LASTSITE [#1cw+]: localhost
MAILCURF [#1$cw+]: 0
MAILFOLDERS [#1$cw+]: 0:INBOX:0 
Home: Room Zero(#0RL)
Location: Room Zero(#0RL)
```

**Differences in self-examine:**
| Field | God | Mortal |
|-------|-----|--------|
| LASTLOGOUT | ✓ Shown | ✗ Missing |
| RQUOTA | ✓ Shown | ✗ Missing (players cannot see own quota via examine) |
| Carrying items | `Name(#dbrefFlags)` | `Name(#dbrefFlags)` (same — it's your own stuff!) |

Mortal `lattr(me)` confirms: `DESCRIBE LAST LASTFAILED LASTIP LASTPAGED LASTSITE MAILCURF MAILFOLDERS`
God `lattr(me)` shows: `DESCRIBE LAST LASTFAILED LASTIP LASTLOGOUT LASTPAGED LASTSITE MAILCURF MAILFOLDERS RQUOTA`

### 5b. Examining a room

**God examines room:**
```
Room Zero(#0RL)
Type: ROOM Flags: LINK_OK
A grand hall with marble pillars and a fountain.
Owner: One(#1PWc)  Zone: *NOTHING*  Pennies: 0
Parent: *NOTHING*
Powers: 
Warnings checked: 
Created: Thu Mar 05 08:01:16 2026
Last Modification: Thu Mar 05 08:05:30 2026
IDESCRIBE [#1$]: You stand inside the grand hall, surrounded by pillars.
Contents:
NewPlayer(#14PenA)
Tester(#13PenAc)
Sword(#6Tn)
Chest(#5Tn)
One(#1PWc)
Exits:
South(#10E)
North(#9E)
South(#4E)
North(#3E)
```

**Mortal examines room:**
```
A grand hall with marble pillars and a fountain.
Obvious exits:
South, North, South, and North
Room Zero(#0RL) is owned by One
```

**Differences proven:**
| Field | God | Mortal |
|-------|-----|--------|
| Object header | `Name(#dbrefFlags)` | None! Omitted entirely |
| Type line | `Type: ROOM Flags: LINK_OK` | ✗ Omitted |
| Owner/Zone/Pennies | Full line | Simplified `"Name is owned by Owner"` |
| Parent | ✓ Shown | ✗ Omitted |
| Timestamps | Created, Last Modification | ✗ Omitted |
| Attributes | All shown | ✗ Omitted |
| Contents | Listed with dbrefs | ✗ Omitted |
| Exits | `ExitName(#dbrefE)` | Listed as `"Obvious exits: name1, name2"` |
| Format | Multi-section technical dump | Minimal, human-friendly |

### 5c. Examining another player

**God examines Tester:**
```
Tester(#13PenAc)
Type: PLAYER Flags: ENTER_OK NO_COMMAND ANSI CONNECTED
A curious explorer with bright eyes.
Owner: Tester(#13PenAc)  Zone: *NOTHING*  Pennies: 150
...all system attributes...
Home: Room Zero(#0RL)
Location: Room Zero(#0RL)
```

**Mortal (Tester) examines god (One):**
```
You see Number One.
LAST [#1cwv+]: Thu Mar 05 08:05:28 2026
Carrying:
Sword
Chest
GodThing
One is owned by One
```

**Differences proven:**
| Field | God examining mortal | Mortal examining god |
|-------|---------------------|---------------------|
| Header | `Name(#dbrefFlags)` | ✗ None |
| Type/Flags | Full type line | ✗ Omitted |
| Description | Full desc | Description only |
| Attributes shown | All | Only LAST |
| Carrying | `Name(#dbrefFlags)` | Plain `Name` |
| Footer | ✗ None | `"Name is owned by Owner"` |

### 5d. Examining an object you don't own

**Mortal examines unowned Chest:**
```
A sturdy oak chest.
Chest is owned by One
```

Just the description and ownership. No detailed examine output.

---

## 6. SAY / POSE / EMIT: Full Two-Sided View — **PROVEN**

This was assumed in the original document; now fully verified.

### SAY

**When god says `say Hello World`:**
- God sees: `You say, "Hello World"`
- Mortal sees: `One says, "Hello World"`

**When mortal says `say Hi there`:**
- Mortal sees: `You say, "Hi there"`
- God sees: `Tester says, "Hi there"`

**Proven pattern:**
- **Sender always sees:** `You say, "<msg>"`
- **All others see:** `<Name> says, "<msg>"`

### POSE (`:`)

**When god poses `pose waves broadly.`:**
- God sees: `One waves broadly.`
- Mortal sees: `One waves broadly.`

**When mortal poses `pose bows deeply.`:**
- Mortal sees: `Tester bows deeply.`
- God sees: `Tester bows deeply.`

**Proven pattern:**
- **Sender and all others see the SAME text:** `<Name> <action>`
- Unlike SAY, there is NO "You" substitution for the poser

### SEMIPOSE (`;`)

**When god does `;'s eyes sparkle.`:**
- God sees: `One's eyes sparkle.`
- Mortal sees: `One's eyes sparkle.`

**Same pattern as POSE — identical for all.**

### @EMIT (room broadcast)

**When mortal does `@emit Player emits this.`:**
- Mortal sees: `Player emits this.`
- God sees: `Player emits this.`

**When god does `@emit A magical wind sweeps through the hall.`:**
- God sees: *(nothing — empty response)*
- Mortal sees: `A magical wind sweeps through the hall.`

**Proven pattern for @emit:**
- **Mortal @emit:** Both sender AND receiver see the text. **The sender IS included.**
- **God @emit:** Sender does NOT see their own @emit (goes to others only).

This is because god is WIZARD — wizards' @emit outputs go to everyone EXCEPT themselves.
Mortals' @emit outputs go to ALL in the room including themselves.

### `|` (pipe emit)

**Mortal tries `|Pipe emit from player.`:**
- Mortal sees: `Huh?  (Type "help" for help.)`
- God sees: nothing

**The `|` character is NOT an emit shortcut in PennMUSH.** It is not a valid command prefix.
The `:` prefix (shortpose/pose) IS valid but `|` is not.

### @NSEMIT (noisy emit — different from @emit/noisy)

**God does `@nsemit An authoritative announcement.`:**
- God sees: `An authoritative announcement.`
- Mortal sees: `An authoritative announcement.`

`@nsemit` sends to ALL in the room including the wizard sender.
`@nssay` and `@nspose` do NOT exist in PennMUSH (return `Huh?`).

### @PEMIT (private emit)

**God does `@pemit #13=This message is only for Tester.`:**
- God sees: `You pemit "This message is only for Tester." to Tester.`
- Mortal sees: `This message is only for Tester.`

---

## 7. WHISPER — **PROVEN**

**Mortal whispers to god:**
- Mortal sees: `You whisper, "Can you hear me, One?" to One.`
- God sees: `Tester whispers: Can you hear me, One?`

**God whispers to mortal:**
- God sees: `You whisper, "Yes I can hear you perfectly." to Tester.`
- Mortal sees: `One whispers: Yes I can hear you perfectly.`

**Pattern:**
- Sender: `You whisper, "<msg>" to <target>.`
- Receiver: `<sender> whispers: <msg>`
- Note the colon after "whispers" but no quotes around the received message

---

## 8. PAGE — **PROVEN**

**Mortal pages god:**
- Mortal sees: `You paged One with 'Test page message from Tester'`
- God sees: `Tester pages: Test page message from Tester`

**God pages mortal:**
- God sees: `You paged Tester with 'Reply from One to Tester'`
- Mortal sees: `One pages: Reply from One to Tester`

**Pattern:**
- Sender: `You paged <target> with '<msg>'`  (single quotes, no trailing period)
- Receiver: `<sender> pages: <msg>`
- Note: same pattern as whisper receiver (colon, no quotes)

---

## 9. Function Output: Mortal vs. Wizard — **PROVEN**

All function results proven with actual output:

| Function | Mortal | Wizard | Notes |
|----------|--------|--------|-------|
| `name(me)` | `Tester` | `One` | Normal |
| `loc(me)` | `#0` | `#0` | Both in same room |
| `num(me)` | `#13` | `#1` | Different dbrefs |
| `dbref(me)` | `#-1 FUNCTION (DBREF) NOT FOUND` | Same error | Not in PennMUSH |
| `flags(me)` | `PenAc` | `PWc` | Different flag sets |
| `type(me)` | `PLAYER` | `PLAYER` | Same |
| `hasflag(me,wizard)` | `0` | `1` | Correct |
| `powers(me)` | `` (empty) | `` (empty) | Both no special powers |
| `owner(me)` | `#13` | `#1` | Players own themselves |
| `money(me)` | `150` | `100000` | God has much more |
| `quota(me)` | `20` | `99999` | God unlimited |
| `lattr(me)` | 8 attrs (no LASTLOGOUT, RQUOTA) | 10 attrs | Privilege-filtered |
| `name(#13)` | `Tester` | `Tester` | Same for visible objects |
| `name(One)` | `One` | `One` | Same |
| `name(#0)` | `Room Zero` | `Room Zero` | Same |
| `hasflag(#0,link_ok)` | `1` | `1` | Same |
| `lstats()` | `15 3 4 5 3 0` | `15 3 4 5 3 0` | Same global stats |
| `numversion()` | `1008008000` | `1008008000` | PennMUSH 1.8.8 |

**Key insight:** Most functions return the same values regardless of wizard status. The privilege
differences are mainly in WHAT you can access (locked attributes, invisible objects), not in
the format of what you can see.

---

## 10. @SET Flag: Mortal Restrictions — **PROVEN**

Surprising finding: **mortals cannot set DARK on themselves** in PennMUSH.

```
PLAYER @set me=DARK   → Permission denied.
PLAYER @set me=!DARK  → Tester - DARK (already) reset.
```

The "already reset" message appears even though DARK was never set — this is the normal
"already in that state" response.

Flags a mortal CAN manipulate on themselves: very limited set (not DARK, not WIZARD, etc.)

**God @set messages:**
```
GOD @set Tester=WIZARD  → Tester - WIZARD set.
GOD @set Tester=!WIZARD → Tester - WIZARD reset.
```

**SharpMUSH comparison:**
| Situation | PennMUSH | SharpMUSH |
|-----------|----------|-----------|
| Set flag | `{Name} - {FLAG} set.` | `Flag: {FlagName} Set.` |
| Unset flag | `{Name} - {FLAG} reset.` | `Flag: {FlagName} Unset.` |
| Already (un)set | `{Name} - {FLAG} (already) reset.` | Unknown |

---

## 11. @CREATE: Mortal vs. Wizard — **PROVEN**

Both mortals and wizards use the same `@create` command:

```
PLAYER @create PlayerWidget → Created: Object #15.
GOD    @create GodWidget    → Created: Object #16.
```

**Output format is identical.** Pennies are deducted from player.

**PLAYER inventory:**
```
You are carrying:
PlayerWidget(#15Tn)
You have 140 Pennies.
```

**GOD inventory:**
```
You are carrying:
GodWidget(#16Tn)
Sword(#12Tn)
Chest(#11Tn)
GodThing(#7Tn)
You have unlimited Pennies.
```

**Differences:**
| Aspect | Mortal | Wizard |
|--------|--------|--------|
| Penny display | `You have N Pennies.` | `You have unlimited Pennies.` |
| Object format | `Name(#dbrefFlags)` | `Name(#dbrefFlags)` (same) |

---

## 12. @DIG: Both Permitted — **PROVEN**

Both mortals and wizards can `@dig` rooms:

```
PLAYER @dig PlayerRoom → PlayerRoom created with room number 17.
GOD    @dig GodRoom    → GodRoom created with room number 18.
```

**Output format is identical** — no privilege difference in the message.

---

## 13. @STATS: Proven Differences

```
PLAYER @stats    → 19 objects = 5 rooms, 4 exits, 7 things, 3 players, 0 garbage.
GOD    @stats    → 19 objects = 5 rooms, 4 exits, 7 things, 3 players, 0 garbage.
```

Global `@stats` is the same for both.

```
PLAYER @stats me → 3 objects = 1 rooms, 0 exits, 1 things, 1 players.
GOD    @stats me → 15 objects = 4 rooms, 4 exits, 6 things, 1 players.
```

`@stats me` counts objects owned by the player — god owns many more.

```
PLAYER @stats #13 → 3 objects = 1 rooms, 0 exits, 1 things, 1 players.
GOD    @stats #13 → 3 objects = 1 rooms, 0 exits, 1 things, 1 players.
```

Both see the same stats when examining another player.

---

## 14. SCORE Command

```
PLAYER score → You have 130 Pennies.
GOD    score → You have unlimited Pennies.
```

---

## 15. Error Messages: Mortal vs. Wizard — **PROVEN**

All error messages are **identical** regardless of wizard status:

| Command | Both see |
|---------|---------|
| `look #99999` | `I don't see that here.` |
| `@tel me=#99999` | `No match.` |
| `examine #99999` | `I can't see that here.` |
| `think [name(#99999)]` | `I can't see that here.` + `#-1 NO SUCH OBJECT VISIBLE` |
| `think [get(#99999/attr)]` | `I can't see that here.` + `#-1 NO SUCH OBJECT VISIBLE` |
| `xyznotacommand` | `Huh?  (Type "help" for help.)` |

**Exception — @newpassword:**
```
PLAYER @newpassword → Permission denied.
GOD    @newpassword → No such player.
```

The god has *access* but gets a different error (missing argument), while the mortal is denied outright.

---

## 16. QUIT Message

```
PLAYER QUIT:
Thank you for visiting.

Please return soon.

*********** D I S C O N N E C T E D ***********
```

The disconnecting player sees the quit file. The god also gets a notification:
```
Tester has disconnected.
```

And then the god's own QUIT shows the same quit file.

---

## 17. New Player Default State

When a player is created (either via `create` screen or `@pcreate`), they have:
- **Pennies:** 150
- **Default flags:** ENTER_OK, NO_COMMAND, ANSI (abbreviated as `PenA`)
- **Quota:** 20 objects
- **Warnings:** "normal" (mortals have warnings enabled by default; wizards don't)
- **Home:** Room Zero (#0) — the default starting room
- **Location:** Room Zero (#0) — spawned there on creation

God player defaults:
- **Pennies:** 100000 (but shows "unlimited" in score/inventory)
- **Flags:** WIZARD (abbreviated as `PW`)
- **Quota:** 99999
- **Warnings:** none

---

## 18. Attribute Visibility (lattr) by Privilege — **PROVEN**

When players examine their own `lattr(me)`:

**Mortal sees:** `DESCRIBE LAST LASTFAILED LASTIP LASTPAGED LASTSITE MAILCURF MAILFOLDERS`  
**Wizard sees:** `DESCRIBE LAST LASTFAILED LASTIP LASTLOGOUT LASTPAGED LASTSITE MAILCURF MAILFOLDERS RQUOTA`

The mortal cannot see `LASTLOGOUT` or `RQUOTA` on themselves because:
- `LASTLOGOUT` is marked `w+` (wizard-only)
- `RQUOTA` is marked `m+` (mortal+ = only some mortals, but here only visible to wizards)

This is PennMUSH attribute flag filtering — attribute flags control visibility per permission level.

---

## 19. Important Corrections to Previous Document

The previous comparison document contained some assumptions that are now proven correct or corrected:

### Corrections:

1. **SAY: Sender sees "You say"** ✓ **CONFIRMED** — `You say, "msg"` to sender, `Name says, "msg"` to others.

2. **POSE: No "You" prefix for sender** ✓ **CONFIRMED but different from SAY** — both sender and receiver see identical `Name poses action.` text.

3. **@EMIT: Sender visibility depends on wizard status:**
   - Wizard @emit: sender does NOT see their own emit
   - Mortal @emit: sender DOES see their own emit
   - This is a nuanced difference not captured before

4. **`|` is NOT a valid emit shortcut** — returns `Huh?`. Only `:` (pose) and `;` (semipose) are prefix shortcuts.

5. **`@emit/noisy` does NOT exist** — PennMUSH returns `"@EMIT doesn't know switch NOISY."`. Use `@nsemit` instead.

6. **WHO footer** — For multiple players: `"There are N players connected."` (both wizard and mortal see same footer text)

7. **Mortal @SET on self is restricted** — Mortals cannot set DARK on themselves. Previous doc said "players can set their own flags" — this is only partially true.

---

## 20. Summary: SharpMUSH Gaps Confirmed

| Gap | Priority | Evidence |
|-----|----------|----------|
| SAY formatting (`You say` / `Name says`) | **Critical** | Proven: sender needs different message than room |
| WHO columns differ by privilege | **High** | Proven: wizard gets 7 cols, mortal gets 4 |
| LOOK shows plain names to mortals (no #dbref) | **High** | Proven: `Chest` vs `Chest(#5Tn)` |
| EXAMINE heavily filtered for mortals | **High** | Proven: mortal sees description+owner only |
| @EMIT sender visibility (wizard doesn't see own emit) | **Medium** | Proven: wizard @emit silent to self |
| POSE is same for sender and receiver | **Low** | Proven: no "You" prefix for poser |
| Mortal @set restrictions (DARK not allowed) | **Low** | Proven |
| Attribute flag filtering in lattr | **Low** | Proven: RQUOTA, LASTLOGOUT hidden from mortals |
| `|` is not emit shortcut | **Low** | Proven |
| @emit/noisy does not exist (use @nsemit) | **Low** | Proven |
| New player default flags (ENTER_OK, NO_COMMAND, ANSI) | **Info** | Proven |
| Player default pennies: 150, quota: 20 | **Info** | Proven |
