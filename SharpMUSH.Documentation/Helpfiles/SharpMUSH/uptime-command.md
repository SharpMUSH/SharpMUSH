<!-- help-article
{
  "corpus": "help",
  "id": "uptime-command",
  "lookup": "@uptime",
  "aliases": [],
  "sections": [
    {
      "id": "process-statistics",
      "heading": "Process statistics",
      "lookup": "@uptime process statistics"
    }
  ],
  "redirects": {
    "@uptime2": "@uptime process statistics"
  }
}
-->
# @uptime

`@uptime[/mortal]`

Reports the server start time, last reboot, total reboot count, current time, time until the next purge and warning runs, and SharpMUSH uptime. Wizards also receive the process statistics below, unless they use `/mortal`.

Output: the time the game started, in seconds, as `uptime()` returns it.

## Process statistics

Wizard output includes the process ID, current and peak working-set memory, and current and peak paged memory. These figures come from the server's .NET process information and depend on the host operating system. They describe the SharpMUSH process; they are not the host machine's shell uptime or a database-dump countdown.

::: seealso
- [@stats]
- [@list]
:::
