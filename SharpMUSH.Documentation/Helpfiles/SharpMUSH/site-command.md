<!-- help-article
{
  "corpus": "help",
  "id": "site-command",
  "lookup": "@site",
  "aliases": [
    "@rhost_site"
  ],
  "sections": []
}
-->
# @site

`@site/<list> <address>=<mask>`<br>
`@site/all <address>=<mask>`<br>
`@site/list`

@site takes an address out of one of RhostMUSH's site lists. Here those lists are options on the sitelock rule for the address: /forbid removes `!connect`, /register removes `!create register`, /noguest removes `!guest`, and /suspect removes `suspect`. The rule goes once it refuses nothing. /all removes the rule for the address, and /list shows every rule.

The mask is dotted (`255.255.255.0`) or a prefix length (`/24`). A single address matches a rule written for it bare, as @sitelock takes it.

RhostMUSH's other lists (/permit, /trust, /noauth, /nohost, /passprox, /passapi, /forbidapi, /noautoreg and /hardconn) are ones SharpMUSH does not keep, so @site says there is nothing to remove.

@site is reached under the RhostMUSH dialect (see [compatibility config]); its own name everywhere is @rhost_site. Without the dialect, @sit still abbreviates @sitelock. You need the players.moderate permission.

```sharp
> @sitelock 192.0.2.0/24=!connect !guest
> @site/forbid 192.0.2.0=255.255.255.0
SITE: Removed forbid for 192.0.2.0/24.
```

::: seealso
- [@sitelock]
- [@blacklist]
:::
