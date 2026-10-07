<!-- help-article
{
  "corpus": "help",
  "id": "recurring-jobs",
  "lookup": "recurring jobs",
  "aliases": [
    "@JOB"
  ],
  "sections": [
    {
      "id": "commands-and-ownership",
      "heading": "Commands and ownership",
      "lookup": "recurring jobs commands and ownership"
    },
    {
      "id": "schedule-grammar",
      "heading": "Schedule grammar",
      "lookup": "recurring jobs schedule grammar"
    },
    {
      "id": "schedule-examples",
      "heading": "Schedule examples",
      "lookup": "recurring jobs schedule examples"
    },
    {
      "id": "timezones",
      "heading": "Timezones",
      "lookup": "recurring jobs timezones"
    },
    {
      "id": "admission-and-restart",
      "heading": "Admission and restart",
      "lookup": "recurring jobs admission and restart"
    },
    {
      "id": "execution-permissions",
      "heading": "Execution permissions",
      "lookup": "recurring jobs execution permissions"
    },
    {
      "id": "status-reporting",
      "heading": "Status reporting",
      "lookup": "recurring jobs status reporting"
    }
  ]
}
-->
# Recurring Jobs

A recurring job runs one attribute on a schedule, as the player who created it. Its definition lives in the world database, so world backups carry it and there are no cron files to copy.

A job records its ID, owning account, player and target, attribute, description, schedule and timezone, next firing, last attempt and last error. An account may hold up to 32 jobs, and a world up to 256.

The sections below cover:

- [recurring jobs commands and ownership] - the `@job` command and who may change a job
- [recurring jobs schedule grammar] and [recurring jobs schedule examples] - writing a schedule
- [recurring jobs timezones] - daylight saving and wall-clock time
- [recurring jobs admission and restart] - missed, late and overlapping firings
- [recurring jobs execution permissions] - what is checked each time a job runs
- [recurring jobs status reporting] - what the last firing fields mean

## Commands and ownership

- `@job/create <objid>/<attribute>=<schedule>|<timezone>[|<description>]` - create a job
- `@job/list` - list your account's jobs
- `@job/list/all` - list every account's jobs
- `@job/disable <job-id>` and `@job/enable <job-id>` - stop or resume firing
- `@job/schedule <job-id>=<schedule>|<timezone>` - change when it fires
- `@job/delete <job-id>` - remove it

`<objid>` is the object's full identity, `#<number>:<creation>`, as [OBJID()] returns it; a bare dbref or a name is refused.

Add `/all` to `/disable`, `/enable`, `/schedule` or `/delete` to select another account's job. The portal's Recurring jobs page has the same controls.

| Permission | Allows |
| --- | --- |
| `jobs.manage.own` | your account's jobs |
| `jobs.manage` | every account's jobs |

Both are resolved like any other permission (see [administrative capabilities]). A job always runs as the active player who created it; editing another account's schedule never changes who it runs as.

Output: the message `@job` shows, such as `Created recurring job <id>`.

## Schedule grammar

A schedule is five fields separated by spaces:

| Field | Values |
| --- | --- |
| minute | 0-59 |
| hour | 0-23 |
| day of month | 1-31 |
| month | 1-12 |
| weekday | 0-7 (Sunday is 0 or 7) |

Each field accepts:

- `*` - every value
- a comma list - `1,15` is either value
- an ascending range - `9-17` is 9 through 17
- a step - `*/15` is every fifteenth value

Names, seconds, `?`, `L`, `W`, `#` and ranges that wrap around are not accepted.

When both day fields are restricted (neither is a literal `*`), either one may match. A minute that matches both fires once.

## Schedule examples

```
0 9 * * 1-5      Weekdays at 09:00
*/15 * * * *     Every fifteen minutes
0 0 1,15 * *     Midnight on the first and fifteenth
0 12 15 * 0      Noon on Sundays and on the fifteenth
```

## Timezones

A schedule follows the named timezone's wall clock.

- A wall time that does not exist, because the clocks jump forward, is skipped.
- A wall time that happens twice, because the clocks go back, fires once, at the earlier one.
- Changing the timezone or the schedule works out a new next firing, and any firing still waiting under the old one is dropped.

An invalid schedule, an unknown timezone, or a schedule that never fires again is refused.

## Admission and restart

Each firing is recorded as claimed before it joins the queue. If the queue refuses it, the refusal is recorded and that firing is not retried.

- A job that falls behind fires at most once for the current time, then moves on to its next future firing.
- A firing is skipped while an earlier firing of the same job still holds a place in the queue, even one that was cancelled and has not yet drained, so superseded firings never pile up.
- After a restart, definitions are kept, interrupted claims are cleared and firings missed while the server was down are skipped. Starting twice never registers a job twice.

## Execution permissions

Just before a job runs, the server reloads it and its account and player, and checks again that:

- the job is enabled and its schedule unchanged;
- the account is still linked and enabled, and still holds the jobs permission;
- the player and target still exist, and the player still controls the target;
- the player may still run that exact attribute on the target.

Unlinking or disabling the account, revoking its role, destroying the player or target, deleting the attribute, or disabling, deleting or rescheduling the job all stop a firing that is already waiting.

The attribute runs on the normal queue with a fresh [execution budget]; it keeps nothing from the command that created the job. A command already running finishes under its own budget. A job grants no PennMUSH powers the player lacks.

## Status reporting

The server is the only writer of job definitions.

- *Last firing* is when the server last tried to run the job; refused and failed attempts show there too.
- *Last error* names an attribute error or a budget failure, without server internals.

Checking permissions and reading the attribute count against the firing's own execution budget. Saving the status afterwards gets one separate attempt; a firing whose status could not be saved is never run a second time because of it.

::: seealso
- [administrative capabilities]
- [execution budget]
- [timezones]
- [@queue]
:::
