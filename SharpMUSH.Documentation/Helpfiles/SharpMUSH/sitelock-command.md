<!-- help-article
{
  "corpus": "help",
  "id": "sitelock-command",
  "lookup": "@sitelock",
  "aliases": [],
  "sections": [
    {
      "id": "allow-and-deny-options",
      "heading": "Allow and deny options",
      "lookup": "@sitelock allow and deny options"
    },
    {
      "id": "character-specific-rules",
      "heading": "Character-specific rules",
      "lookup": "@sitelock character-specific rules"
    }
  ],
  "redirects": {
    "@sitelock2": "@sitelock allow and deny options",
    "@sitelock3": "@sitelock character-specific rules"
  }
}
-->
# @sitelock

`@sitelock`<br>
`@sitelock/name [[!]<pattern>]`<br>
`@sitelock[/player] <host-pattern>=<options>[, <name>]`<br>
`@sitelock[/<ban|register>][/player] <host-pattern>`<br>
`@sitelock/check <host>`<br>
`@sitelock/remove[/player] <string>`

The @sitelock command adds rules to the access.cnf file, controlling a host's level of access to the MUSH, or adds banned player names to the names.cnf file. Only Wizards may use @sitelock.

@sitelock without arguments lists all sites in access.cnf. Rules are processed in the order listed, and the first matching rule is applied. `@sitelock/check` tells you which rule will match for a given `<host>`.

`@sitelock/name <pattern>` bans player names matching `<pattern>`, which may use the wildcards "*" and "?" and is matched without regard to case. `@sitelock/name !<pattern>` lifts that ban, and `@sitelock/name` with no argument lists the banned patterns. A banned name is refused as a player name for anyone but a wizard or the player who already has it; `valid(playername, ...)` reports it. The list is kept with the game's configuration, so it survives a restart and is the same list the web portal's banned-names page edits.

`@sitelock <host-pattern>=<options>[, <name>]` controls the access options for hosts which match `<host-pattern>`, which may include wildcard characters "*" and "?". See help @sitelock allow and deny options for the list of options, and help @sitelock character-specific rules for an explanation about the name argument.

For backward compatibility, `@sitelock/ban` is shorthand for setting options "!connect !create !guest", and `@sitelock/register` is shorthand for options "!create register".

If the `/player` switch is given, `<host-pattern>` is treated as a player name, and sitelock rules are added for that player's LASTIP and LASTSITE, if set.

::: seealso
- [WILDCARDS]
- [regexp]
- [IPADDR()]
- [HOST()]
:::

## Allow and deny options

Sitelock allow/deny options:
- connect -- allow this site to connect to non-guest players
- !connect -- don't allow this site to connect to non-guest players
- guest -- allow this site to connect to guest players
- !guest -- don't allow this site to connect to guest players
- create -- allow this site to create players
- !create -- don't allow this site to create players
- default -- allow any of the above
- none -- don't allow any of the above
- !god -- God can't connect from this site.
- !wizard -- Wizards can't connect from this site.
- !admin -- Wizards and Royalty can't connect from this site.

Allow/deny options not set are assumed to be allowed.

Sitelock special options:
- register -- allow this site to use 'register `<name>` `<email>`' at the connection screen to register players. Players will be emailed their character's password. This should be used with !create to be effective.
- suspect -- set all players who connect from this site SUSPECT.
- deny_silent -- don't log failed access attempts from this site.
- regexp -- Treat the hostname pattern as a regular expression instead of a wildcard pattern.

## Character-specific rules

If you specify a character name after the options, the options are only checked if the host pattern matches, AND the character being checked for connect support matches the one you gave. Use it only with connect and !connect options, since they're the only ones where an existing character is used.

For example, to disallow anyone from connecting to 'Twink' from one domain, but to allow connections to the character from others, use something like:
```sharp
> @sitelock *.somesite.com=!connect,Twink
```

If you want to disallow connections to a character from anywhere, use @newpassword or `@sitelock *=!connect,Twink`.

`@sitelock/remove` will delete entries that were added with @sitelock if their host-pattern matches `<string>` exactly. If the `/player` switch is given, `<string>` is treated as a player name, and entries whose host-patterns match the player's LASTIP or LASTSITE addresses exactly will be deleted.
