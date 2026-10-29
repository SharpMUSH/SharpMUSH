<!-- help-article
{
  "corpus": "help",
  "id": "config-net-command",
  "lookup": "@config net",
  "aliases": [],
  "sections": [
    {"id": "web", "heading": "Game name and web", "lookup": "@config net web"},
    {"id": "logins", "heading": "Logins and clients", "lookup": "@config net logins"},
    {"id": "sql", "heading": "SQL and SSL", "lookup": "@config net sql"},
    {"id": "ignored", "heading": "Options with no effect", "lookup": "@config net ignored"}
  ]
}
-->
# @config net

Networking and connection-related options.

- [@config net web]: the game's name, its web address, and HTTP requests
- [@config net logins]: logins, guests, DNS, Pueblo and client media
- [@config net sql]: the SQL server and SSL client certificates
- [@config net ignored]: listening addresses, ports and other options that are accepted and ignored

## Game name and web

- `mud_name=<string>`: The name of the mush for mudname() and @version and the like.
- `mud_url=<string>`: The game's web address, returned by mudurl(), shown by @version and sent as the MSSP WEBSITE. First-run setup fills it in with the address the web portal was reached at, unless that is the server's own machine. An MXP or Pueblo client is sent the game's own pictures, such as the connect screen's logo, at this address; while it is unset, such a client is shown their text instead.
- `http_handler=<dbref/number>`: If this is set, it handles HTTP requests sent to /http/ on the web server.
- `http_per_second=<number>`: If this is set, limit HTTP requests allowed per second.

## Logins and clients

- `use_dns=<boolean>`: Are IP addresses resolved into hostnames?
- `logins=<boolean>`: Are mortal logins enabled?
- `player_creation=<boolean>`: Can `register` and `create` be used from the login screen?
- `guests=<boolean>`: Are guest logins allowed?
- `pueblo=<boolean>`: Does a new telnet connection get the Pueblo hello, so a Pueblo client turns on its links and pictures? A client without Pueblo shows the hello as one line of text at the top of the login screen.
- `media_base_url=<string>`: The web address a GMCP client such as Mudlet fetches the game's sounds and music from, when [sound()] or [music()] names a file rather than a full address. It is sent as GMCP Client.Media.Default when a client agrees to GMCP, and to every such client when it changes; a `/` is added if it does not end in one. Empty sends nothing, and such a client then needs full addresses.

## SQL and SSL

- `sql_platform=<string>`: What kind of SQL server are we using? ("mysql", "mariadb", "postgresql", "postgres" or "sqlite"). SQL is off while sql_host and sql_database are both empty.
- `sql_host=<string>`: What is the hostname or IP address of the SQL server?
- `sql_database=<string>`: The name of the database on the SQL server. Only God sees it.
- `sql_username=<string>`: The user name to log in to the SQL server with. Only God sees it. The password is the `sql.password` secret: see [@secret].
- `ssl_require_client_cert=<boolean>`: Are client certificates verified in SSL connections?

## Options with no effect

These options are accepted and ignored. Where the servers listen is set where they are deployed, not in the game.

- `ip_addr=<string>` and `ssl_ip_addr=<string>`: the connection server listens on every address.
- `portal_port=<number>` and `ssl_portal_port=<number>`: the web portal listens where the server's ASPNETCORE_URLS setting says.
- `socket_file=<string>`: there is no separate SSL process. TLS telnet runs on the connection server's TLS port, with the certificate it is given.
- `use_ws=<boolean>` and `ws_url=<string>`: WebSocket connections are always on, at `/ws` on the connection server's HTTP port.
- `json_unsafe_unescape=<boolean>`: accepted and ignored.
