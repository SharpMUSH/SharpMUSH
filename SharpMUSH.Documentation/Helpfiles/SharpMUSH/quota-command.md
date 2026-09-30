<!-- help-article
{
  "corpus": "help",
  "id": "quota-command",
  "lookup": "@quota",
  "aliases": [],
  "sections": [
    {
      "id": "administrative-quota-changes",
      "heading": "Administrative quota changes",
      "lookup": "@quota administrative quota changes",
      "aliases": [
        "@squota",
        "@allquota"
      ]
    }
  ],
  "redirects": {
    "@quota2": "@quota administrative quota changes"
  }
}
-->
# @quota

`@quota [<player>]`

These commands are only meaningful if the Quota system is enabled (check the use_quota @config option).

@quota shows the current quota for `<player>`, or for the executor if no `<player>` is given. You must control `<player>`, or have either the See_All or Quotas @power.

## Administrative quota changes

`@squota <player>=[+|-]<amount>`<br>
`@allquota[/quiet] [<limit>]`

@squota is a Wizard-only command which adjusts the quota of `<player>`. If `<amount>` is prefixed by + or -, their current quota will be incremented or decremented by `<amount>`, respectively. Otherwise, their total quota is set to `<amount>`.

@allquota can only be used by God. With no `<limit>` argument, it reports the quotas of all players. If a `<limit>` is given, the quotas of all players is reset to `<limit>`. The `/quiet` switch stops @allquota reporting the current quotas before changing them.

Players always have enough quota for the objects they currently own; if you attempt to set their quota to a lower number (with @squota or @allquota), it will be set to the number of objects they own instead.

`@quota/set` and `@quota/all` are equivilent to @squota and @allquota, respectively.


**See Also:**
- [QUOTAS]
- [@power]
- [@power]
