<!-- help-article
{
  "corpus": "help",
  "id": "activity-function",
  "lookup": "activity()",
  "aliases": [
    "activity"
  ],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "activity options"
    },
    {
      "id": "reports",
      "heading": "Reports",
      "lookup": "activity reports"
    },
    {
      "id": "what-is-counted",
      "heading": "What is counted",
      "lookup": "activity counting"
    }
  ]
}
-->
# activity()

`activity(<report>[, <options>])`

Returns one of the staff activity reports as JSON. The numbers are counted from poses and scenes, never from time connected. *<report>* is one of `summary`, `drifting`, `cohorts`, `rooms` or `hours`, and *<options>* is a JSON object that changes the window the report covers.

activity() needs the `players.view` permission, since the reports name players. The +health package draws these reports for staff; the portal's Activity page under People shows them too.

```sharp
> think activity(summary)
{"asOf":"2026-10-15","days":30,"approved":44,"posed":36,"posedBefore":36,"poses":5666,"posesBefore":7498,"scenes":39,"scenesBefore":58,"newApprovals":7,"roomsUsed":10,"roomsDead":11,"drifting":12,"peak":{"weekday":0,"hour":18,"players":8.25},"zone":"UTC"}
```

Output: the last 30 days next to the 30 before. 36 approved characters posed, as many as before, with 5666 poses against 7498.

Read one value with [json_query()]:

```sharp
> think json_query(activity(summary,{{"days":7}}),extract,$.posed)
31
```

::: seealso
- [json_query()]
- [isapproved()]
- [roles]
:::

## Options

*<options>* is a JSON object; leave it out for every default. Typed at a client, wrap it in a second pair of braces, as `{{"days":7}}`, since the outer pair is taken as grouping.

- `days`: how many days the window covers, ending today. 30 by default, 1 to 366.
- `until`: the last day of the window, written `2026-10-15`. Today by default.
- `baseline`: how many days before the window a player's usual pace is read from, for `drifting`. 60 by default.
- `minimum`: how many of those days a player must have posed on to count as a regular, for `drifting`. 4 by default.
- `months`: how many approval months `cohorts` shows. 12 by default, up to 36.
- `dead`: how many days without a pose make a room dead, for `rooms`. 90 by default.
- `zone`: the time zone `hours` and the summary's busiest hour are shown in, such as `America/New_York`. UTC by default.
- `limit`: how many rows each list holds. 50 by default, up to 1000.

An option it does not know, or a value of the wrong kind, returns an error:

```sharp
> think activity(summary,{{"days":"7"}})
#-1 DAYS MUST BE A WHOLE NUMBER
```

## Reports

- `summary`: approved characters, how many posed in the window and in the one before it, poses, scenes, new approvals, rooms used and dead, how many players are drifting, and the busiest hour (`peak`, with `weekday` 0 for Sunday).
- `drifting`: regulars who now pose half as often or less. A regular posed on at least `minimum` of the `baseline` days before the window. Each row has the days and poses in the window (`recentDays`, `recentPoses`) and in the baseline (`baselineDays`, `baselinePoses`), and the last pose. Longest drop first.
- `cohorts`: a row per month, newest first, of the characters approved in it. `active` is how many posed in the window; `retained` is the share who posed in each 30 days after the month ended, six of them, `null` until those days have passed. A character with no approval in the audit log counts by the month it was created, and `undated` says how many.
- `rooms`: the rooms with the most poses in the window (`used`), then rooms older than `dead` days with no pose in that time (`dead`), never-used first.
- `hours`: `players`, seven rows from Sunday, each of 24 hours: how many approved characters posed in that hour, on average, over the window.

```sharp
> think json_query(activity(hours,{{"zone":"America/New_York"}}),extract,$.players\[0\])
[0,0,0,0,0,0,0,0,0,0,0,0,2.5,4,8.25,5.25,7.25,3,0.5,0,3.5,4.75,5,3]
```

Output: Sunday in New York. The busiest hour, 14:00, averaged 8.25 characters posing.

`scenes` is `null` everywhere when the Scene plugin is not running, rather than 0.

## What is counted

A pose is a `pose`, `say`, `semipose` or `@emit` by a player, or a pose a player adds to a scene from the portal. A typed pose a scene logger also records counts once. `summary`, `drifting`, `cohorts` and `hours` count approved characters only; `rooms` counts every pose made in a room.

Nothing anyone wrote is kept: only how many times each character posed each day, which hours of the day they posed in, how many poses and scenes each room had each day and who posed there, and each character's and room's last pose. Counts are written once a minute.

The game keeps these days for good unless it sets `SHARPMUSH_HISTORY_ACTIVITY_MAX_AGE`, such as `400d`. Older days are then removed; each character's and room's last pose is kept. See [@storage].
