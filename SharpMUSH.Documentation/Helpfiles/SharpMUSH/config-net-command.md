<!-- help-article
{
  "corpus": "help",
  "id": "config-net-command",
  "lookup": "@config net",
  "aliases": [],
  "sections": [
    {"id": "web", "heading": "Game name and web", "lookup": "@config net web"},
    {"id": "logins", "heading": "Logins and clients", "lookup": "@config net logins"},
    {"id": "sql", "heading": "SQL and SSL", "lookup": "@config net sql"}
  ]
}
-->
# @config net

Networking and connection-related options.

- [@config net web]: the game's name, its web address, and HTTP requests
- [@config net logins]: logins, guests, DNS, Pueblo and client media
- [@config net sql]: the SQL server and SSL client certificates

## Game name and web

- `mud_name=<string>`: The name of the mush for mudname() and @version and the like.
- `mud_url=<string>`: If this is set, the welcome message for the mush is bracketed in <!-- ... --> for all clients, and web browsers are redirected to the url described in mud_url. First-run setup fills it in with the address the web portal was reached at, unless that is the server's own machine. An MXP or Pueblo client is sent the game's own pictures, such as the connect screen's logo, at this address; while it is unset, such a client is shown their text instead.
- `http_handler=<dbref/number>`: If this is set, support HTTP requests to MUSH port.
- `http_per_second=<number>`: If this is set, limit HTTP requests allowed per second.

## Logins and clients

- `use_dns=<boolean>`: Are IP addresses resolved into hostnames?
- `logins=<boolean>`: Are mortal logins enabled?
- `player_creation=<boolean>`: Can CREATE be used from the login screen?
- `guests=<boolean>`: Are guest logins allowed?
- `pueblo=<boolean>`: Is Pueblo support turned on?
- `media_base_url=<string>`: The web address a GMCP client such as Mudlet fetches the game's sounds and music from, when [sound()] or [music()] names a file rather than a full address. It is sent as GMCP Client.Media.Default when a client agrees to GMCP, and to every such client when it changes; a `/` is added if it does not end in one. Empty sends nothing, and such a client then needs full addresses.

## SQL and SSL

- `sql_platform=<string>`: What kind of SQL server are we using? ("mysql", "postgreql", "sqlite" or "disabled")
- `sql_host=<string>`: What is the hostname or ip address of the SQL server
- `ssl_require_client_cert=<boolean>`: Are client certificates verified in SSL connections?
