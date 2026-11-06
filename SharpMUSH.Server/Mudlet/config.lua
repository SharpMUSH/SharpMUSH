mpackage = [[sharpmush-ui]]
author = [[SharpMUSH]]
title = [[The game's interface: map, gauges, chat and who is here, drawn from what the game sends over GMCP.]]
description = [[# SharpMUSH interface

A panel on the right of the game's text, filled from what the game sends over GMCP:

- **Room**: where you are, the exits, who is here, what lies about and what you carry.
- **Gauges**: one for each value the game sends with a maximum, such as hp and maxhp.
- **Chat**: channel lines and pages.
- **Map**: drawn as you walk, from the rooms and exits the game sends.

A part the game sends nothing for stays empty.

Commands:

```
interface          -- what it shows, and these commands
interface hide     -- take the panel away (remembered between sessions)
interface show     -- bring it back
```

The game offers this package with Client.GUI. `+gui off` in the game stops it being offered;
Mudlet's Package Manager removes it.
]]
version = [[1.0.0]]
created = "2026-11-06T00:00:00+00:00"
