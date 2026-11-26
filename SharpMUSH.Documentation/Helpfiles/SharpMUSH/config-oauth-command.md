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

Which apps players can sign in with. An app that is not registered names itself by an https address, its client_id, that serves a short description of it. Players choose what each app may do, and never more than they hold.

- `oauth_app_policy=<string>`: `open` (Allow all, the default), `review` (Approval process: new apps wait on the portal's Apps page until staff allow them) or `registered` (Whitelist & blacklist: only apps staff register or allow work, and no address is read).
- `oauth_blocked_domains=<list>`: Domains, such as `example.com`, whose apps are refused, with every subdomain. Their tokens stop working at once.

Staff allow, block or forget apps on the portal's Apps page. Blocking an app takes back every player's permission and stops its tokens. An allowed app whose description later asks for more scopes or redirect addresses, or changes its name, waits for staff again under `review`.

The game reads a description only from a public internet address, at most once an hour, and keeps at most 100 apps not reviewed, 5 from any one host.
