<!-- help-article
{
  "corpus": "help",
  "id": "secret-command",
  "lookup": "@secret",
  "aliases": [],
  "sections": [
    {"id": "list", "heading": "Listing secrets", "lookup": "@secret list", "aliases": ["@secret/list"]},
    {"id": "storing", "heading": "Storing and clearing a value", "lookup": "@secret storing", "aliases": ["@secret/clear"]},
    {"id": "hosts", "heading": "Hosts for @http", "lookup": "@secret host", "aliases": ["@secret/host"]},
    {"id": "overrides", "heading": "Overrides from outside the game", "lookup": "@secret overrides"},
    {"id": "examples", "heading": "Examples", "lookup": "@secret examples"},
    {"id": "listing-example", "heading": "Listing and clearing example", "lookup": "@secret listing example"}
  ]
}
-->
# @secret

`@secret[/list]`<br>
`@secret <name>=<value>`<br>
`@secret/clear <name>`<br>
`@secret/host <name>=[<host> ...]`

Manages the secrets the server keeps for itself: passwords, tokens and signing keys, such as the SQL password (`sql.password`). A stored value is encrypted with the server's key ring, which is kept outside the world, so a copy of the world alone opens no secret. Nothing shows a value again: not this command, not the web portal, and no function. Softcode cannot read a secret.

A typed value passes through your client, which may keep it in a log. Use the web portal's Secrets page, or an override, where that matters.

Using @secret needs the `config.admin` permission. A plugin may name another permission that manages its own secret.

Output: nothing; it tells you what it did.

- [@secret list]: what the listing shows for each secret
- [@secret storing]: setting a value and removing it
- [@secret host]: the hosts @http may send a secret to
- [@secret overrides]: environment variables, secret files, and mush.cnf imports
- [@secret examples]: storing a token and naming its hosts
- [@secret listing example]: what the listing looks like, then clearing the token

## Listing secrets

`@secret[/list]`

With no argument, or `/list`, @secret lists every secret the server or a plugin reads, and every stored one: where its value comes from, when (UTC) and by whom it was stored, and what it is for. A secret whose stored value this server cannot decrypt shows as `unreadable: set it again`.

## Storing and clearing a value

`@secret <name>=<value>`<br>
`@secret/clear <name>`

`@secret <name>=<value>` stores `<value>` as `<name>`, replacing what was stored. The command is not evaluated, so the value is stored exactly as typed. A name is lower-case letters and digits, in parts joined by `.`.

`/clear` removes the stored value.

## Hosts for @http

`@secret/host <name>=[<host> ...]`

`/host` names the hosts @http may send the secret to, separated by spaces, replacing the ones named before; with none, it may be sent nowhere. `*.example.com` covers every host under example.com, but not example.com itself. Only a secret whose name starts with `http.` can be sent, and only over https; see [@http]. The hosts belong to the name, not the value: setting or clearing the value leaves them, and they hold for a value an override supplies.

## Overrides from outside the game

An operator can set a secret outside the game instead, and that overrides the stored value: an environment variable `SHARPMUSH_SECRET_<NAME>` (the name in upper case, with `.` written as `_`, so `sql.password` is `SHARPMUSH_SECRET_SQL_PASSWORD`), or a file named for the secret in `/run/secrets` (where Docker and Kubernetes put secrets; `SHARPMUSH_SECRETS_PATH` names another directory).

Importing a PennMUSH `mush.cnf` in the web portal stores its `sql_password` as the `sql.password` secret, never as a configuration option.

## Examples

```
> @secret http.github.token=ghp_example
http.github.token is set. Its value is never shown again.
> @secret/host http.github.token=api.github.com uploads.github.com
@http may send http.github.token to: api.github.com uploads.github.com
```

## Listing and clearing example

```
> @secret
+-------------------------------< Secrets (2) >------------------------------+
| Secret             Value       Stored                 For                  |
| -------------------------------------------------------------------------- |
| http.github.token  stored      2026-10-15 10:00 by    @http may send it to |
|                                God(#1)                api.github.com       |
|                                                       uploads.github.com.  |
| sql.password       not set     -                      The password of      |
|                                                       sql_username on the  |
|                                                       SQL server.          |
+----------------------------------------------------------------------------+
> @secret/clear http.github.token
http.github.token is cleared.
```

::: seealso
- [@config]
- [@sql]
- [@http]
:::
