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

Which apps may ask players for access without staff registering them on the portal's Apps page. Such an app names itself by an https address, its client_id, that serves a short description of the app. Players still choose what each app may do, and never more than they hold themselves.

- `oauth_app_policy=<string>`: `review` (the default): an app is listed on the Apps page and asks nobody until staff allow it. `open`: an app may ask at once, and the consent page says staff have not reviewed it. `registered`: only apps staff register or allowed are accepted, and no address is read.
- `oauth_blocked_domains=<list>`: Domains, such as `example.com`, whose apps are refused, with every subdomain. Their tokens stop working at once.

Staff allow, block or forget each app on the portal's Apps page. Blocking one also takes back every player's permission and stops its tokens.
