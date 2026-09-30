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

This command, for mortals, gives the time until the next database dump. For wizards, it also gives the system uptime (just as if 'uptime' had been typed at the shell prompt) and process statistics, some of which are explained in the next help entry. Wizards can use the `/mortal` switch to avoid seeing the extra process statistics.

## Process statistics

While the exact statistics displayed depends on the operating system of the game's server, typical things might include the process ID, the machine page size, the maximum resident set size utilized (in K), "integral" memory (in K x seconds-of-execution), the number of page faults ("hard" ones require I/O activity, "soft" ones do not), the number of times the process was "swapped" out of main memory, the number of times the process had to perform disk I/O, the number of network packets sent and received, the number of context switches, and the number of signals delivered to the process.

Under Linux, memory usage is split into a number of different categories including shared libraries, resident set size, stack size, and some other figures. Also under linux, more information on signals is printed.


**See Also:**
- [@stats]
- [@list]
