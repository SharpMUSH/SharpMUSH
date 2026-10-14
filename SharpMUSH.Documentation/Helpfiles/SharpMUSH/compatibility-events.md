<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-events",
  "lookup": "compatibility events",
  "aliases": [],
  "sections": [
    {
      "id": "dump-events-are-never-raised",
      "heading": "Dump events are never raised",
      "lookup": "compatibility events dump events are never raised"
    },
    {
      "id": "db-dbck-is-never-raised",
      "heading": "db\\`dbck is never raised",
      "lookup": "compatibility events db dbck is never raised"
    },
    {
      "id": "log-huh-is-never-raised",
      "heading": "log\\`huh is never raised",
      "lookup": "compatibility events log huh is never raised"
    },
    {
      "id": "sql-events-are-never-raised",
      "heading": "SQL events are never raised",
      "lookup": "compatibility events sql events are never raised"
    },
    {
      "id": "signal-events-are-never-raised",
      "heading": "Signal events are never raised",
      "lookup": "compatibility events signal events are never raised"
    },
    {
      "id": "player-inactivity-is-never-raised",
      "heading": "player\\`inactivity is never raised",
      "lookup": "compatibility events player inactivity is never raised"
    },
    {
      "id": "http-fail-is-never-raised",
      "heading": "http\\`fail is never raised",
      "lookup": "compatibility events http fail is never raised"
    }
  ]
}
-->
# Compatibility Events

PennMUSH documents some events that SharpMUSH never raises, because it has nothing to raise them from. An event handler attribute named after one of them is harmless and never runs. [event list] lists the events SharpMUSH does raise.

## Dump events are never raised

**A choice.**

**PennMUSH** saves the database to a file every dump_interval and raises dump\`5min, dump\`1min, dump\`complete and dump\`error around each save.<br>
**SharpMUSH** has no dump: every change is written to the database as it happens.<br>
**Why.** There is no save to warn about or report on.<br>
**Workaround.** For a copy of the world, use backup\`complete and backup\`error (see [event backup]), which a scheduled backup raises too.

```sharp
> @dump
Dump command does nothing for SharpMUSH. Consider using @backup.
```

## db\`dbck is never raised

**A choice.**

**PennMUSH** runs a consistency check every dbck_interval and raises db\`dbck after it.<br>
**SharpMUSH** runs no consistency check, and `@dbck` reports that it is not supported.<br>
**Why.** The database keeps its links consistent when it writes them, so there is nothing to repair.<br>
**Workaround.** db\`purge and db\`wcheck are raised after the scheduled purge and @warnings check.

```sharp
> @dbck
Not Supported for SharpMUSH.
```

## log\`huh is never raised

**A choice.**

**PennMUSH** raises log\`huh when it writes an unmatched command to the huh log.<br>
**SharpMUSH** keeps no huh log.<br>
**Why.** There is no log entry to raise it from.<br>
**Workaround.** Hook the command the game runs for an unmatched line: `@hook/after HUH_COMMAND=<object>, <attribute>` (see [@hook]).<br>
**No example.** An event that is never raised leaves nothing to show.

## SQL events are never raised

**A choice.**

**PennMUSH** keeps one connection to its SQL server open and raises sql\`connect, sql\`connectfail and sql\`disconnect as it opens, fails to open and loses it.<br>
**SharpMUSH** opens a pooled connection for each sql() query and keeps none open between them.<br>
**Why.** There is no lasting connection to report on.<br>
**Workaround.** sql() returns an error when the server cannot be reached; check its result.

```sharp
> think sql(SELECT 1)
1
```

The query opened its own connection and closed it again; nothing stays connected to report on.

## Signal events are never raised

**A choice.**

**PennMUSH** raises signal\`usr1 and signal\`usr2 when its process receives SIGUSR1 or SIGUSR2, in place of its own reaction to the signal.<br>
**SharpMUSH** does not act on those signals.<br>
**Why.** It runs as several services, usually in containers, where an administrator does not signal the process.<br>
**Workaround.** Run `@shutdown/reboot` or `@backup` from the game, or from a softcode command staff can call.<br>
**No example.** A signal is sent from outside the game.

## player\`inactivity is never raised

**A choice.**

**PennMUSH** raises player\`inactivity after it disconnects or hides idle players.<br>
**SharpMUSH** does not disconnect idle players.<br>
**Why.** Nothing is disconnected, so there is nothing to report.<br>
**Workaround.** A softcode loop can check idle() for each player in lwho().<br>
**No example.** An event that is never raised leaves nothing to show.

## http\`fail is never raised

**A choice.**

**PennMUSH** raises http\`fail when it cannot read an HTTP request.<br>
**SharpMUSH** reads requests with its web server, which answers a malformed one before the game sees it.<br>
**Why.** A request that cannot be read never reaches the game.<br>
**Workaround.** http\`blocked and http\`command still report the requests that do reach it.<br>
**No example.** A malformed request is sent from outside the game.
