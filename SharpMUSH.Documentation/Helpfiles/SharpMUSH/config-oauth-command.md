<!-- help-article
{
  "corpus": "help",
  "id": "config-oauth-command",
  "lookup": "@config oauth",
  "aliases": [],
  "sections": []
}
-->
# @config oauth

Apps players bring: apps that ask players for access without staff registering them first. Such an app names itself by an https address, its client_id, that serves a short description of it. Players still choose what each app may do, and never more than they hold themselves.

- `oauth_app_policy=<string>`: `open` (the default): the app may ask at once, only for `profile` and `scenes.read`, and the consent page says staff have not reviewed it. `review`: the app waits on the portal's Apps page until staff allow it, and players are told it is waiting. `registered`: only apps staff register are accepted, and no address is read.
- `oauth_blocked_domains=<list>`: Domains, such as `example.com`, whose apps are turned away, with every subdomain. Their tokens stop working at once.

Every app a player has used is listed on the portal's Apps page. Staff allow, block or forget each one there. Allowing an app lets it ask for more than `profile` and `scenes.read`. Blocking one takes back every player's permission and stops its tokens; it blocks that one address, so block the domain to turn away every app on it. An allowed app whose description later asks for more scopes or redirect addresses, or changes its name, goes back to not reviewed.

The game reads a description only from a public internet address, at most once an hour, and keeps at most 100 apps not reviewed, 5 from any one host.
