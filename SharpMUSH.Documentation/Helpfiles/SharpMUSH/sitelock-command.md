<!-- help-article
{
  "corpus": "help",
  "id": "sitelock-command",
  "lookup": "@sitelock",
  "aliases": [],
  "sections": [
    {
      "id": "banned-names",
      "heading": "Banned names",
      "lookup": "@sitelock names",
      "aliases": ["@sitelock/name"]
    },
    {
      "id": "host-rules",
      "heading": "Adding host rules",
      "lookup": "@sitelock rules"
    },
    {
      "id": "allow-and-deny-options",
      "heading": "Allow and deny options",
      "lookup": "@sitelock allow and deny options"
    },
    {
      "id": "removing-rules",
      "heading": "Removing rules",
      "lookup": "@sitelock remove"
    }
  ],
  "redirects": {
    "@sitelock2": "@sitelock allow and deny options",
    "@sitelock3": "@sitelock remove"
  }
}
-->
# @sitelock

`@sitelock`<br>
`@sitelock/check <host>`

The @sitelock command adds rules controlling a host's level of access to the MUSH, or adds banned player names. Rules and names are kept in the game's configuration (the `sitelock_rules` and `banned_names` settings), so they survive a restart. Using @sitelock needs the `players.moderate` permission.

@sitelock without arguments (or `@sitelock/list`) lists all rules and banned names. A connection is refused when any rule that matches its host carries the deny option for what it is doing. `@sitelock/check` tells you which rule matches a given `<host>`.

- [@sitelock names]: banning player names
- [@sitelock rules]: adding a rule for a host, and the ban and register shorthands
- [@sitelock allow and deny options]: the options a rule can set
- [@sitelock remove]: removing rules

::: seealso
- [WILDCARDS]
- [IPADDR()]
- [HOST()]
:::

## Banned names

`@sitelock/name [[!]<pattern>]`

`@sitelock/name <pattern>` bans player names matching `<pattern>`, which may use the wildcards "*" and "?" and is matched without regard to case. `@sitelock/name !<pattern>` lifts that ban, and `@sitelock/name` with no argument lists the banned patterns. A banned name is refused as a player name for anyone but a wizard or the player who already has it; `valid(playername, ...)` reports it. The list is kept with the game's configuration, so it survives a restart and is the same list the web portal's banned-names page edits.

## Adding host rules

`@sitelock <host-pattern>=<options>`<br>
`@sitelock/<ban|register> <host-pattern>`

`@sitelock <host-pattern>=<options>` sets the access options, separated by spaces, for hosts which match `<host-pattern>`. The pattern may be a wildcard pattern using "*" and "?" (matched against the host name or the IP address), a bare IP address, or a CIDR block such as 10.0.0.0/8. Setting a rule for a pattern that already has one replaces it. Live connections that the new rule blocks are dropped. See help @sitelock allow and deny options for the list of options.

`@sitelock/ban` is shorthand for setting options "!connect !create !guest", and `@sitelock/register` is shorthand for options "!create register".

## Allow and deny options

Sitelock deny options:
- !connect -- don't allow this site to connect to any player, guests included
- !guest -- don't allow this site to connect to guest players
- !create -- don't allow this site to create players or accounts

Options not set are allowed. Other words are stored with the rule and shown in the listing, but have no effect.

## Removing rules

`@sitelock/remove <string>`

`@sitelock/remove` deletes the rule whose host-pattern matches `<string>` exactly.
