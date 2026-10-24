<!-- help-article
{
  "corpus": "help",
  "id": "profile-command",
  "lookup": "@profile",
  "aliases": ["@profile/start", "@profile/stop"],
  "sections": [
    {"id": "permissions", "heading": "Who may profile", "lookup": "@profile permissions"},
    {"id": "results", "heading": "Reading the results", "lookup": "@profile results"},
    {"id": "sampling", "heading": "Sampling limits and privacy", "lookup": "@profile sampling"},
    {"id": "example", "heading": "Example", "lookup": "@profile example"}
  ]
}
-->
# @profile

`@profile/start [<seconds>]`<br>
`@profile/stop`<br>
`@profile`

@profile records how often each function and command runs for you, and how long it takes, so you can find what makes your code slow.

- `@profile/start` starts recording, for 60 seconds or the number of seconds given, 1 to 300. Starting again replaces your last profile.
- `@profile/stop` stops recording early.
- `@profile` shows what was recorded.

Recording also stops when its time is up, when you lose the permission to profile, or when the server restarts. A stopped profile is kept for 15 minutes, or less when another needs the room. The server keeps at most eight profiles, one per account.

- [@profile permissions]: the permissions needed, and the portal page
- [@profile results]: what each row holds, and why times overlap
- [@profile sampling]: how many samples are kept, and what is never captured
- [@profile example]: profiling a short session

## Who may profile

Profiling needs the `diagnostics.profile` and `queue.inspect` permissions, and records only a character linked to your account, running as itself. Code running on an object you own is not recorded as you, since it does not carry your account's permissions.

The portal has the same profiler at `/admin/diagnostics`, for staff with `queue.inspect`; it asks which of your characters to profile as. Anyone else uses the commands.

## Reading the results

Each row is one command or function: the attribute it ran from, when known, its kind and name, how many times it ran and failed, its total time in milliseconds and its longest single run. The attribute is shown only when it is on the object you profiled; otherwise it is `?`.

A row's time includes everything it called and waited for, so the rows overlap: `THINK` includes the `ADD` inside it. Do not add the rows up, and do not read them as CPU time.

## Sampling limits and privacy

A profile keeps up to 256 different rows, and the server holds at most 4,096 samples waiting to be counted; past that, some may be left out. The report says so, but never how many, since that could show activity you may not see.

Each sample is checked against your permissions before it is counted, and again when you read the profile, so losing a permission hides what was already recorded. No code, arguments, register values, results or error messages are ever recorded.

## Example

```sharp
> @profile/start 30
Profile recording for 30 seconds.
> think add(2,3)
5
> @profile
```

Output: a box titled `Queue profile` with a row each for `@PROFILE`, `THINK` and `ADD`, such as `? Function ADD 1 0 0.55 0.55`, then when the profile expires and how to read it.

Profiling never changes the order in which commands run, or the queue's limits.
