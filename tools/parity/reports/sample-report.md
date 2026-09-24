# PennMUSH vs SharpMUSH parity report

- Generated: 2026-09-24 17:22:39Z
- SharpMUSH commit: 11fc7efcf
- PennMUSH commit: 80a1d5b
- Scenarios: 00-login, 05-import, 10-player-commands, 20-admin-commands, 30-softcode
- **Steps: 150 — 71 match, 1 known difference, 0 open gap (baseline), 78 UNEXPECTED DIFFERENCE, 0 error; 0 stale allowlist entries, 0 baseline entries now fixed**

| Scenario | Steps | Match | Known | Open gap | Unexpected | Error |
|---|---:|---:|---:|---:|---:|---:|
| 00-login | 10 | 3 | 0 | 0 | 7 | 0 |
| 05-import | 15 | 5 | 1 | 0 | 9 | 0 |
| 10-player-commands | 55 | 23 | 0 | 0 | 32 | 0 |
| 20-admin-commands | 34 | 16 | 0 | 0 | 18 | 0 |
| 30-softcode | 36 | 24 | 0 | 0 | 12 | 0 |

## Differences (78; open gaps are tracked in baseline.json)

### `00-login/login.player#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.player` — tools/parity/scenarios/00-login.scn:3, session `alice`, login `Alice alicepass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,7 @@
-Last connect was from None on <TIMESTAMP>.
+Welcome back, Alice!
 
-
-MAIL: You have no mail.
-
-Lab
+Alice has connected.
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `00-login/login.player#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.player` — tools/parity/scenarios/00-login.scn:5, session `alice`, command `think [loc(%#)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#6
+#6:<CTIME>
```

### `00-login/login.player#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.player` — tools/parity/scenarios/00-login.scn:6, session `alice`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,4 @@
-Lab
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `00-login/login.admin#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.admin` — tools/parity/scenarios/00-login.scn:8, session `wiz`, login `Wiz wizpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,8 @@
-Last connect was from None on <TIMESTAMP>.
+Welcome back, Wiz!
 
 
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Wiz has connected.
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `00-login/login.god#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.god` — tools/parity/scenarios/00-login.scn:11, session `god`, login `One godpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,10 +1,9 @@
-Last connect was from localhost on <TIMESTAMP>.
+Welcome back, One!
 
 
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
 Contents:
-Wiz(#3PWenAc)
+One(#1WP)
+Wiz(#3AenWP)
 [wiz] One has connected.
```

### `00-login/login.badpassword#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.badpassword` — tools/parity/scenarios/00-login.scn:14, session `bad`, login-fail `Alice wrong`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-That is not the correct password.
+Invalid Password.
```

### `00-login/login.unknown#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 00-login/login.unknown` — tools/parity/scenarios/00-login.scn:16, session `nobody`, login-fail `Nobody whatever`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-There is no player with that name.
+Could not find that player.
```

### `05-import/import.players#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.players` — tools/parity/scenarios/05-import.scn:3, session `wiz`, login `Wiz wizpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,7 @@
-Last connect was from localhost on <TIMESTAMP>.
+Welcome back, Wiz!
 
 
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `05-import/import.players#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.players` — tools/parity/scenarios/05-import.scn:5, session `wiz`, command `think [flags(*Wiz)] [flags(*Alice)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-PWenAc PenA
+AenWP AenP
```

### `05-import/import.rooms#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.rooms` — tools/parity/scenarios/05-import.scn:9, session `wiz`, command `think [name(loc(*Alice))] [type(loc(*Alice))] [get(loc(*Alice)/DESC)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Lab ROOM A tidy laboratory.
+Lab ROOM
```

### `05-import/import.rooms#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.rooms` — tools/parity/scenarios/05-import.scn:10, session `wiz`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,4 @@
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `05-import/import.rooms#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.rooms` — tools/parity/scenarios/05-import.scn:11, session `wiz`, command `@teleport me=[loc(*Alice)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,5 @@
-Lab(#6Rn)
+Wiz has arrived.
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Wiz(#3AenWP)
```

### `05-import/import.rooms#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.rooms` — tools/parity/scenarios/05-import.scn:12, session `wiz`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,4 @@
-Lab(#6Rn)
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Wiz(#3AenWP)
```

### `05-import/import.rooms#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.rooms` — tools/parity/scenarios/05-import.scn:13, session `wiz`, command `@teleport me=#0`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,5 @@
-Room Zero(#0RL)
+Wiz has arrived.
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `05-import/import.things#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.things` — tools/parity/scenarios/05-import.scn:18, session `wiz`, command `think [loc(first(lcon(*Alice)))=loc(*Alice)] [owner(first(lcon(*Alice)))=owner(*Alice)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#4=loc(*Alice) #1=owner(*Alice)
+#4:<CTIME>=loc(*Alice) #1=owner(*Alice)
```

### `05-import/import.things#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 05-import/import.things` — tools/parity/scenarios/05-import.scn:19, session `wiz`, command `think [get(first(lcon(*Alice))/DESC)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-A small widget.
+
```

### `10-player-commands/setup.sessions#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/setup.sessions` — tools/parity/scenarios/10-player-commands.scn:3, session `alice`, login `Alice alicepass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,8 +1,6 @@
-Last connect was from localhost on <TIMESTAMP>.
-Last FAILED connect was from localhost on <TIMESTAMP>.
+Welcome back, Alice!
 
-
-MAIL: You have no mail.
-
-Lab
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `10-player-commands/setup.sessions#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/setup.sessions` — tools/parity/scenarios/10-player-commands.scn:4, session `bob`, login `Bob bobpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,7 @@
-Last connect was from None on <TIMESTAMP>.
+Welcome back, Bob!
 
-
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Bob has connected.
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Bob
```

### `10-player-commands/comm.say#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/comm.say` — tools/parity/scenarios/10-player-commands.scn:10, session `alice`, command `say hello there`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-You say, "hello there"
+Alice says, "hello there"
```

### `10-player-commands/comm.say#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/comm.say` — tools/parity/scenarios/10-player-commands.scn:13, session `alice`, command `"quoted shortcut`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-You say, "quoted shortcut"
+Alice says, "quoted shortcut"
```

### `10-player-commands/comm.page#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/comm.page` — tools/parity/scenarios/10-player-commands.scn:24, session `alice`, command `page Bob=Are you there?`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-You paged Bob with 'Are you there?'
-[bob] Alice pages: Are you there?
+I can't see that here.
+No one to page.
```

### `10-player-commands/comm.page#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/comm.page` — tools/parity/scenarios/10-player-commands.scn:26, session `alice`, command `page Nobody=hello`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-I can't find who you're trying to page with: Nobody
-Unable to page: Nobody
+I can't see that here.
+No one to page.
```

### `10-player-commands/obj.inventory#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.inventory` — tools/parity/scenarios/10-player-commands.scn:29, session `alice`, command `inventory`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,3 +1,2 @@
 You are carrying:
-Widget
-You have 150 Pennies.
+Widget(#7nT)
```

### `10-player-commands/obj.inventory#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.inventory` — tools/parity/scenarios/10-player-commands.scn:30, session `alice`, command `look Widget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-Widget
+Widget(#7nT)
 A small widget.
```

### `10-player-commands/obj.inventory#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.inventory` — tools/parity/scenarios/10-player-commands.scn:31, session `alice`, command `examine Widget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1 @@
-A small widget.
-Widget is owned by One
+Widget is owned by One.
```

### `10-player-commands/obj.inventory#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.inventory` — tools/parity/scenarios/10-player-commands.scn:32, session `alice`, command `think [get(Widget/COLOR)] [get(Widget/SIZE)] [u(Widget/GREET,World)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 NO PERMISSION TO GET ATTRIBUTE #-1 NO PERMISSION TO GET ATTRIBUTE #-1 NO PERMISSION TO GET ATTRIBUTE
+#-1 NO PERMISSION TO GET ATTRIBUTE #-1 NO PERMISSION TO GET ATTRIBUTE Hello, World! I am Widget.
```

### `10-player-commands/obj.create#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:35, session `alice`, command `@create Gizmo`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Created: Object #NEW1.
+Created Gizmo (#NEW1:<CTIME>).
```

### `10-player-commands/obj.create#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:37, session `alice`, command `look Gizmo`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-Gizmo(#NEW1Tn)
+Gizmo(#NEW1nT)
 A gleaming gizmo.
```

### `10-player-commands/obj.create#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:38, session `alice`, command `@name Gizmo=Gadget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Name set.
+
```

### `10-player-commands/obj.create#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:39, session `alice`, command `look Gadget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-Gadget(#NEW1Tn)
+Gadget(#NEW1nT)
 A gleaming gizmo.
```

### `10-player-commands/obj.create#5` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:40, session `alice`, command `@destroy Gadget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Use @recycle instead
+Gadget is scheduled to be destroyed.
```

### `10-player-commands/obj.create#6` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.create` — tools/parity/scenarios/10-player-commands.scn:41, session `alice`, command `look Gadget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-Gadget(#NEW1Tn)
+Gadget(#NEW1GnT)
 A gleaming gizmo.
```

### `10-player-commands/obj.attrs#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:44, session `alice`, command `&FOO Widget=bar`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Permission denied.
+#-1 NO PERMISSION TO SET ATTRIBUTE
```

### `10-player-commands/obj.attrs#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:45, session `alice`, command `think [get(Widget/FOO)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 NO PERMISSION TO GET ATTRIBUTE
+
```

### `10-player-commands/obj.attrs#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:46, session `alice`, command `&FOO Widget=`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Permission denied.
+#-1 NO PERMISSION TO SET ATTRIBUTE
```

### `10-player-commands/obj.attrs#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:47, session `alice`, command `think [get(Widget/FOO)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 NO PERMISSION TO GET ATTRIBUTE
+
```

### `10-player-commands/obj.attrs#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:48, session `alice`, command `@set Widget/COLOR=no_command`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Permission denied.
+#-1 NO PERMISSION TO SET ATTRIBUTE
```

### `10-player-commands/obj.attrs#5` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:49, session `alice`, command `examine Widget/COLOR`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-No matching attributes.
+Widget is owned by One.
```

### `10-player-commands/obj.attrs#6` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.attrs` — tools/parity/scenarios/10-player-commands.scn:50, session `alice`, command `@set Widget/COLOR=!no_command`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Permission denied.
+#-1 NO PERMISSION TO SET ATTRIBUTE
```

### `10-player-commands/obj.give#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.give` — tools/parity/scenarios/10-player-commands.scn:54, session `alice`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,4 +1,5 @@
-Lab
+Lab(#6nR)
 A tidy laboratory.
 Contents:
+Alice
 Widget
```

### `10-player-commands/obj.give#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.give` — tools/parity/scenarios/10-player-commands.scn:55, session `alice`, command `get Widget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1,2 @@
 You take Widget.
+Alice takes Widget.
```

### `10-player-commands/obj.give#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.give` — tools/parity/scenarios/10-player-commands.scn:56, session `alice`, command `give *Bob=1`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Give to whom?
+Money transfer will not be implemented.
```

### `10-player-commands/obj.give#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/obj.give` — tools/parity/scenarios/10-player-commands.scn:57, session `alice`, command `give *Nobody=1`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1,2 @@
-Give to whom?
+I can't see that here.
+I don't see that here.
```

### `10-player-commands/room.dig#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/room.dig` — tools/parity/scenarios/10-player-commands.scn:60, session `alice`, command `@dig Annex=Annex Door,Back`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,5 +1,2 @@
 Annex created with room number NEW2.
 Permission denied.
-Opened exit #NEW3
-Trying to link...
-You can't link to that.
```

### `10-player-commands/room.dig#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/room.dig` — tools/parity/scenarios/10-player-commands.scn:62, session `alice`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,4 @@
-Lab
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `10-player-commands/room.dig#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/room.dig` — tools/parity/scenarios/10-player-commands.scn:64, session `alice`, command `look`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,4 @@
-Lab
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `10-player-commands/flags.set#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/flags.set` — tools/parity/scenarios/10-player-commands.scn:70, session `alice`, command `think [hasflag(me,HAVEN)] [flags(me)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-1 PHenAc
+1 AeHnP
```

### `10-player-commands/flags.set#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 10-player-commands/flags.set` — tools/parity/scenarios/10-player-commands.scn:73, session `alice`, command `@set me=BOGUS`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-BOGUS - I don't recognize that flag.
+Alice - I don't recognize that flag.
```

### `20-admin-commands/admin.wizard#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.wizard` — tools/parity/scenarios/20-admin-commands.scn:3, session `wiz`, login `Wiz wizpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,7 @@
-Last connect was from localhost on <TIMESTAMP>.
+Welcome back, Wiz!
 
 
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `20-admin-commands/admin.wizard#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.wizard` — tools/parity/scenarios/20-admin-commands.scn:4, session `alice`, login `Alice alicepass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,6 @@
-Last connect was from localhost on <TIMESTAMP>.
+Welcome back, Alice!
 
-
-MAIL: You have no mail.
-
-Lab
+Lab(#6nR)
 A tidy laboratory.
+Contents:
+Alice
```

### `20-admin-commands/admin.wizard#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.wizard` — tools/parity/scenarios/20-admin-commands.scn:7, session `wiz`, command `examine *Alice`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,25 +1,25 @@
-Alice(#4PenAc)
-Type: PLAYER Flags: ENTER_OK NO_COMMAND ANSI CONNECTED
-Owner: Alice(#4PenAc)  Zone: *NOTHING*  Pennies: 129
+Alice(#4AenP)
+Type: PLAYER Flags: ANSI ENTER_OK NO_COMMAND PLAYER
+There is nothing to see here
+Owner: Alice(#4AenP)  Zone: *NOTHING*
 Parent: *NOTHING*
-Basic Lock [#1i]: =Alice(#4PenAc)
-Enter Lock [#1i]: =Alice(#4PenAc)
-Use Lock [#1i]: =Alice(#4PenAc)
+Basic Lock [#1i]: =Ancestor Player(#4T)
+Enter Lock [#1i]: =Ancestor Player(#4T)
+Use Lock [#1i]: =Ancestor Player(#4T)
 Powers:
-Channels: *NONE*
 Warnings checked: normal
 Created: <TIMESTAMP>
-LAST [#1cwv+]: <TIMESTAMP>
-LASTFAILED [#1cw+]:
-LASTIP [#1cw+]: 127.0.0.1
-LASTLOGOUT [#1cw+]: <TIMESTAMP>
-LASTPAGED [#1cw+]: #5:<CTIME>
-LASTSITE [#1cw+]: localhost
-MAILCURF [#1$cw+]: 0
-MAILFOLDERS [#1$cw+]: 0:INBOX:0
-RQUOTA [#1m+]: 17
+Last modified: <TIMESTAMP>
+Quota: 20
+LAST [cwv+ #1]: <TIMESTAMP>
+LASTFAILED [cw+ #1]:
+LASTIP [cw+ #1]: None
+LASTLOGOUT [cw+ #1]: <TIMESTAMP>
+LASTSITE [cw+ #1]: None
+MAILCURF [$cw+ #1]: 0
+MAILFOLDERS [$cw+ #1]: 0:INBOX:0
 Carrying:
-Widget(#7Tn)
-Gadget(#NEW1Tn)
-Home: Room Zero(#0RL)
-Location: Lab(#6Rn)
+Widget(#7nT)
+Gadget(#NEW1GnT)
+Home: Room Zero(#0LR)
+Location: Lab(#6nR)
```

### `20-admin-commands/admin.wizard#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.wizard` — tools/parity/scenarios/20-admin-commands.scn:8, session `wiz`, command `@teleport *Alice=here`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,6 +1,8 @@
 Alice has arrived.
 Teleported.
-[alice] Room Zero(#0RL)
+[alice] Alice has arrived.
+[alice] Room Zero(#0LR)
 [alice] You are in Room Zero.
 [alice] Contents:
 [alice] Wiz
+[alice] Alice
```

### `20-admin-commands/admin.wizard#5` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.wizard` — tools/parity/scenarios/20-admin-commands.scn:9, session `wiz`, command `@teleport *Alice=[loc(*Alice)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,5 +1,6 @@
 Teleported.
-[alice] Room Zero(#0RL)
+[alice] Room Zero(#0LR)
 [alice] You are in Room Zero.
 [alice] Contents:
 [alice] Wiz
+[alice] Alice
```

### `20-admin-commands/admin.pcreate#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.pcreate` — tools/parity/scenarios/20-admin-commands.scn:12, session `wiz`, command `@pcreate Carol=carolpw`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-New player 'Carol' (#NEW4) created with password 'carolpw'
+New player 'Carol' (#NEW3) created with password 'carolpw'
```

### `20-admin-commands/admin.pcreate#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.pcreate` — tools/parity/scenarios/20-admin-commands.scn:13, session `wiz`, command `@newpassword Carol=carolpw2`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Password for Carol changed.
+Set new password for Carol: carolpw2
```

### `20-admin-commands/admin.pcreate#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.pcreate` — tools/parity/scenarios/20-admin-commands.scn:14, session `carol`, login `Carol carolpw2`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,12 +1,10 @@
-Last connect was from None on <TIMESTAMP>.
+Welcome back, Carol!
 
-
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
 Contents:
+Wiz
 Alice
-Wiz
+Carol
 [alice] Carol has connected.
 [wiz] Carol has connected.
```

### `20-admin-commands/admin.force#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.force` — tools/parity/scenarios/20-admin-commands.scn:30, session `wiz`, command `@force *Alice=say forced hello`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
-Alice says, "forced hello"
-[alice] You say, "forced hello"
+Wiz says, "forced hello"
+[alice] Wiz says, "forced hello"
```

### `20-admin-commands/admin.force#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.force` — tools/parity/scenarios/20-admin-commands.scn:32, session `wiz`, command `@trigger *Alice/TRIG=1`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-No such attribute.
+No such attribute: TRIG
```

### `20-admin-commands/admin.denied#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.denied` — tools/parity/scenarios/20-admin-commands.scn:37, session `alice`, command `@pcreate Mallory=x`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-You do not have the power over body and mind!
+Permission denied.
```

### `20-admin-commands/admin.denied#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.denied` — tools/parity/scenarios/20-admin-commands.scn:39, session `alice`, command `@force *Wiz=say hi`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1 @@
-Permission denied.
-Sorry.
+Permission denied. You do not control the target.
```

### `20-admin-commands/admin.lists#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.lists` — tools/parity/scenarios/20-admin-commands.scn:44, session `wiz`, command `@list flags`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1,68 @@
-Flags: ABODE (A), ANSI (A), AUDIBLE (a), CHAN_USEFIRSTMATCH, CHOWN_OK (C), CLOUDY (x), COLOR (C), DARK (D), DEBUG (b), DESTROY_OK (d), ENTER_OK (e), FIXED (F), FLOATING (F), GAGGED (g), HALT (h), HAVEN (H), HEAR_CONNECT, HEAVY, JUDGE (J), JUMP_OK (J), JURY_OK (j), KEEPALIVE (k), LIGHT (l), LINK_OK (L), LISTEN_PARENT (^), LOUD, MISTRUST (m), MONIKER, MONITOR (M), MYOPIC (m), NOACCENTS (~), NO_COMMAND (n), NO_LEAVE (N), NO_LOG, NOSPOOF ("), NO_TEL (N), NO_WARN (w), ON-VACATION (o), OPAQUE (O), OPEN_OK, ORPHAN (i), PARANOID, PUPPET (p), QUIET (Q), ROYALTY (r), SAFE (X), SHARED (Z), STICKY (S), SUSPECT (s), TERSE (x), TRACK_MONEY, TRANSPARENT (t), TRUST (I), UNFINDABLE (U), UNINSPECTED (u), UNREGISTERED (?), VERBOSE (v), VISUAL (V), WIZARD (W), XTERM256, Z_TEL (Z)
+OBJECT FLAGS:
+NAME                 SYMBOL TYPE RESTRICTIONS
+-------------------- ------ -------------------
+ABODE                A      ROOM
+ANSI                 A      PLAYER
+APPROVED             +      PLAYER
+AUDIBLE              a      ROOM,PLAYER,EXIT,THING
+CHAN_USEFIRSTMATCH          ROOM,PLAYER,EXIT,THING
+CHOWN_OK             C      ROOM,EXIT,THING
+CLOUDY               x      ROOM,PLAYER,EXIT,THING
+COLOR                C      PLAYER
+DARK                 D      ROOM,PLAYER,EXIT,THING
+DEBUG                b      ROOM,PLAYER,EXIT,THING
+DESTROY_OK           d      THING
+ENTER_OK             e      ROOM,PLAYER,EXIT,THING
+FIXED                F      PLAYER
+FLOATING             F      ROOM
+GAGGED               g      PLAYER
+GOING                G      ROOM,PLAYER,EXIT,THING
+GOING_TWICE                 ROOM,PLAYER,EXIT,THING
+HALT                 h      ROOM,PLAYER,EXIT,THING
+HAVEN                H      PLAYER
+HEAR_CONNECT                ROOM,PLAYER,EXIT,THING
+HEAVY                       ROOM,PLAYER,EXIT,THING
+JUDGE                J      PLAYER
+JUMP_OK              J      ROOM
+JURY_OK              j      PLAYER
+KEEPALIVE            k      PLAYER
+LIGHT                l      ROOM,PLAYER,EXIT,THING
+LINK_OK              L      ROOM,PLAYER,EXIT,THING
+LISTEN_PARENT        ^      PLAYER,THING,ROOM
+LOUD                        ROOM,PLAYER,EXIT,THING
+MISTRUST             m      THING,EXIT,ROOM
+MONIKER                     ROOM,PLAYER,EXIT,THING
+MONITOR              M      ROOM,PLAYER,THING
+MYOPIC               m      PLAYER
+NOACCENTS            ~      PLAYER
+NOSPOOF              "      ROOM,PLAYER,EXIT,THING
+NO_COMMAND           n      ROOM,PLAYER,EXIT,THING
+NO_LEAVE             N      THING
+NO_LOG                      ROOM,PLAYER,EXIT,THING
+NO_TEL               N      ROOM
+NO_WARN              w      ROOM,PLAYER,EXIT,THING
+ON_VACATION          o      PLAYER
+OPAQUE               O      ROOM,PLAYER,EXIT,THING
+OPEN_OK                     ROOM
+ORPHAN               i      ROOM,PLAYER,EXIT,THING
+PARANOID                    ROOM,PLAYER,EXIT,THING
+PUPPET               p      ROOM,THING
+QUIET                Q      ROOM,PLAYER,EXIT,THING
+ROYALTY              r      ROOM,PLAYER,EXIT,THING
+SAFE                 X      ROOM,PLAYER,EXIT,THING
+SCENE_ROOM           S      ROOM
+SHARED               Z      PLAYER
+STICKY               S      ROOM,PLAYER,EXIT,THING
+SUSPECT              s      ROOM,PLAYER,EXIT,THING
+TRACK_MONEY                 ROOM,PLAYER,EXIT,THING
+TRANSPARENT          t      ROOM,PLAYER,EXIT,THING
+TRUECOLOR                   PLAYER
+TRUST                I      ROOM,PLAYER,EXIT,THING
+UNFINDABLE           U      ROOM,PLAYER,EXIT,THING
+UNINSPECTED          u      ROOM
+UNREGISTERED         ?      PLAYER
+VERBOSE              v      ROOM,PLAYER,EXIT,THING
+VISUAL               V      ROOM,PLAYER,EXIT,THING
+WIZARD               W      ROOM,PLAYER,EXIT,THING
+XTERM256                    PLAYER
+Z_TEL                Z      ROOM,THING
```

### `20-admin-commands/admin.lists#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.lists` — tools/parity/scenarios/20-admin-commands.scn:45, session `wiz`, command `@list powers`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1,40 @@
-Powers: Announce, Boot, Builder, CAN_DARK, CAN_HTTP, Can_spoof, Chat_Privs, DEBIT, Functions, Guest, Halt, Hide, HOOK, Idle, Immortal, Link_Anywhere, Login, Long_Fingers, MANY_ATTRIBS, No_Pay, No_Quota, Open_Anywhere, Pemit_All, PICK_DBREFS, Player_Create, Poll, Queue, Quotas, Search, See_All, See_Queue, Send_OOB, SQL_OK, Tport_Anything, Tport_Anywhere
+OBJECT POWERS:
+NAME                 SYMBOL ALIAS              TYPE RESTRICTIONS
+-------------------- ------ ------------------ -------------------
+Announce
+Boot
+Builder
+Can_Dark
+Can_HTTP
+Can_Spoof
+Chat_Privs
+Debit
+Functions
+Guest
+Halt
+Hide
+Hook
+Idle
+Immortal
+Link_Anywhere
+Login
+Long_Fingers
+Many_Attribs
+No_Pay
+No_Quota
+Open_Anywhere
+Pemit_All
+Pick_DBRefs
+Player_Create
+Poll
+Queue
+Quotas
+Search
+See_All
+See_OOB
+See_Queue
+Send_OOB                    Pueblo_Send
+SQL_OK
+Tport_Anything
+Tport_Anywhere
+Unkillable
```

### `20-admin-commands/admin.lists#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.lists` — tools/parity/scenarios/20-admin-commands.scn:47, session `wiz`, command `@config names`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
- names_file                               names.cnf
+No configuration category or option named 'names'.
```

### `20-admin-commands/admin.search#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.search` — tools/parity/scenarios/20-admin-commands.scn:50, session `wiz`, command `@search name=Widget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,5 +1,4 @@
-
-THINGS:
-Widget(#7Tn) [owner: One(#1PW)]
-----------  Search Done  ----------
-Totals: Rooms...0  Exits...0  Things...1  Players...0
+@search: Advanced database search
+  Criteria: NAME=Widget
+  #7 (Widget) [THING]
+1 objects found.
```

### `20-admin-commands/admin.search#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.search` — tools/parity/scenarios/20-admin-commands.scn:51, session `wiz`, command `@find Widget`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,3 @@
-Widget(#7Tn)
-*** 1 objects found ***
+@find: Searching for objects matching 'Widget'...
+  #7 (Widget)
+Found 1 matching objects.
```

### `20-admin-commands/admin.search#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 20-admin-commands/admin.search` — tools/parity/scenarios/20-admin-commands.scn:52, session `wiz`, command `@stats`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-13 objects = 4 rooms, 1 exits, 3 things, 5 players, 0 garbage.
+25 objects = 5 rooms, 0 exits, 14 things, 6 players.
```

### `30-softcode/sc.login#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.login` — tools/parity/scenarios/30-softcode.scn:3, session `wiz`, login `Wiz wizpass`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,7 +1,7 @@
-Last connect was from localhost on <TIMESTAMP>.
+Welcome back, Wiz!
 
 
-MAIL: You have no mail.
-
-Room Zero(#0RL)
+Room Zero(#0LR)
 You are in Room Zero.
+Contents:
+Wiz(#3AenWP)
```

### `30-softcode/sc.strings#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.strings` — tools/parity/scenarios/30-softcode.scn:15, session `wiz`, command `think [upcstr(abc)] [lcstr(ABC)] [capstr(abc def)] [trim(  a b  )] [trim(xxaxx,x,b)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 FUNCTION (UPCSTR) NOT FOUND abc Abc def a b a
+#-1 FUNCTION (UPCSTR) NOT FOUND DID YOU MEAN 'UCSTR' abc Abc def a b a
```

### `30-softcode/sc.strings#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.strings` — tools/parity/scenarios/30-softcode.scn:16, session `wiz`, command `think [pos(c,abcd)] [strmatch(hello,h*o)] [match(a b c,b)] [comp(a,b)] [ljust(a,3)]|[rjust(a,3)]|[center(a,5)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-3 1 2 -1 a  |  a|  a
+0 1 2 -1 a  |  a|  a
```

### `30-softcode/sc.lists#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.lists` — tools/parity/scenarios/30-softcode.scn:23, session `wiz`, command `think [lnum(5)] [lnum(2,4)] [revwords(a b c)] [remove(a b c,b)] [member(a b c,c)] [splice(a b c,x y z,b)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-0 1 2 3 4 2 3 4 c b a a c 3 a y c
+0 1 2 3 4 2 3 4 c b a a c 3 a x  b y  c z
```

### `30-softcode/sc.lists#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.lists` — tools/parity/scenarios/30-softcode.scn:24, session `wiz`, command `think [iter(a b c,##-#@)] [map(#lambda/strlen(%%0),aa b cccc)] [fold(#lambda/add(%%0,%%1),1 2 3)] [filter(#lambda/gt(%%0,1),0 1 2 3)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-a-1 b-2 c-3 2 1 4 6 2 3
+a-#@ b-#@ c-#@ 2 1 4 6 2 3
```

### `30-softcode/sc.control#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.control` — tools/parity/scenarios/30-softcode.scn:30, session `wiz`, command `think [null(x)][s(%b)] [lit([add(1,2)])] [eval(add(1,2))]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
- [add(1,2)] #-1 FUNCTION (EVAL) EXPECTS 2 ARGUMENTS BUT GOT 1
+ [add(1,2)] #-1 FUNCTION (EVAL) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1
```

### `30-softcode/sc.control#3` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.control` — tools/parity/scenarios/30-softcode.scn:31, session `wiz`, command `think [u(#lambda/[add(%0,1)],5)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1,2 @@
 I can't see that here.
-#-1 INVALID OBJECT
+#-1 NO MATCH
```

### `30-softcode/sc.control#4` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.control` — tools/parity/scenarios/30-softcode.scn:32, session `wiz`, command `think [ulocal(#lambda/[setq(0,inner)]%q0,x)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1,2 +1 @@
-I can't see that here.
-#-1 INVALID OBJECT
+inner
```

### `30-softcode/sc.identity#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.identity` — tools/parity/scenarios/30-softcode.scn:47, session `wiz`, command `think [name(%!)] [hasflag(%#,WIZARD)] [flags()] [type(%#)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-Wiz 1 AAaCxCDbdeFFghHJJjklL^mMm~nN"NwoOipQrXZSsxtIUu?vVWZ PLAYER
+Wiz 1 AA+aCxCDbdeFFgGhHJJjklL^mMm~"nNNwoOipQrXSZSstIUu?vVWZ PLAYER
```

### `30-softcode/sc.errors#0` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.errors` — tools/parity/scenarios/30-softcode.scn:50, session `wiz`, command `think [nosuchfunction(1)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 FUNCTION (NOSUCHFUNCTION) NOT FOUND DID YOU MEAN 'MESSAGE'
+#-1 FUNCTION (NOSUCHFUNCTION) NOT FOUND
```

### `30-softcode/sc.errors#1` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.errors` — tools/parity/scenarios/30-softcode.scn:51, session `wiz`, command `think [add()] [strlen()] [mid(abc)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -1 +1 @@
-#-1 FUNCTION (ADD) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1 0 #-1 FUNCTION (MID) EXPECTS 3 ARGUMENTS BUT GOT 1
+#-1 FUNCTION (ADD) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1 0 #-1 FUNCTION (MID) EXPECTS AT LEAST 3 ARGUMENTS BUT GOT 1
```

### `30-softcode/sc.errors#2` — DIFFERENCE

- Repro: `tools/parity/run.sh --only 30-softcode/sc.errors` — tools/parity/scenarios/30-softcode.scn:52, session `wiz`, command `think [get(nothing/nothing)] [name(#9999)] [loc(#9999)]`

```diff
--- PennMUSH
+++ SharpMUSH
@@ -2,3 +2,3 @@
 I can't see that here.
 I can't see that here.
-#-1 NO SUCH OBJECT VISIBLE #-1 NO SUCH OBJECT VISIBLE #-1
+#-1 NO MATCH #-1 NO MATCH #-1 NO MATCH
```

## Known differences observed (1)

- `05-import/import.owners#1` — KD-0001: money() is deliberately unsupported: SharpMUSH does not track pennies (function answers '#-1 NOT SUPPORTED'). (tracking: #1134)

## Import anchors (dbref on each server)

| Anchor | PennMUSH | SharpMUSH |
|---|---:|---:|
| god | #1 | #1 |
| wiz | #3 | #16 |
| alice | #4 | #17 |
| bob | #5 | #18 |
| lab | #6 | #19 |
| widget | #7 | #20 |

## Coverage

- commands: 30 of 176 PennMUSH commands exercised (17%). Full list in coverage.json.
- functions: 100 of 527 PennMUSH functions exercised (18%). Full list in coverage.json.

## Normalization rules applied to both sides

- `eol`: CRLF/CR line endings become LF (telnet framing, not content).
- `ansi`: ANSI colour/style escape sequences are removed. SharpMUSH colours object names and headers on connections where PennMUSH sends plain text; colour negotiation is not what this harness compares.
- `timestamp`: Absolute timestamps like 'Thu Sep 24 16:59:11 2026' become <TIMESTAMP>.
- `sync-token`: The harness's own sync sentinels never appear in transcripts; if one leaks into a line it becomes <SYNC>.
- `trailing-ws`: Trailing spaces/tabs on each line are removed; a trailing blank line is dropped.
- `site-text`: on login steps the connect screen, MOTD and wizard MOTD text of each server (connect.txt, motd.txt, wizmotd.txt) and SharpMUSH's `Connected!` line are removed: they are site content, not behaviour.
- `dbref`: dbrefs of world-fixture objects are mapped to PennMUSH's numbering; dbrefs of objects created during scenarios become `#NEW<k>` in order of first appearance.
