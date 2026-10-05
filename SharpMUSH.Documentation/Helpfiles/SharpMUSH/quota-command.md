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

@quota shows the current quota for `<player>`, or for the executor's owner if no `<player>` is given. You must control `<player>`, or have either the See_All or Quotas @power.

## Administrative quota changes

`@quota/set <player>=<amount>`<br>
`@quota/all`<br>
`@squota [<player>]`<br>
`@allquota[/quiet] <amount>`

A Wizard can use `@quota/set` to set a player's total quota to an integer amount, or `@quota/all` to list all players' quotas. The amount is an absolute value: a leading + or - is parsed as a sign, not as a relative adjustment. The value is not clamped to the player's current owned-object count; setting it below current usage prevents further quota-limited building.

`@squota` currently reports used and total quota, with the same visibility rules as `@quota`; it does not set quota. This differs from PennMUSH. Use `@quota/set` to change one player's quota.

`@allquota` requires an integer amount and sets every player's total quota to that value. It is available to Wizards and holders of the Quota power. Normally each player is notified; `/quiet` suppresses those individual notifications. To list quotas without changing them, use `@quota/all`.

::: seealso
- [QUOTAS]
- [@power]
:::
