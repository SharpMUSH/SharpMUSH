<!-- help-article
{
  "corpus": "help",
  "id": "config-file-command",
  "lookup": "@config file",
  "aliases": [],
  "sections": []
}
-->
# @config file

Files the server reads. These options cannot be changed with `@config/set`.

- `dict_file=<string>`: A file of words, one per line, loaded into the `words` category of [@suggest] at startup, and again whenever the file has changed.
- `access_file=<string>`: Accepted but ignored. Site rules are the `sitelock_rules` option: see [@sitelock].
- `names_file=<string>`: Accepted but ignored. Banned names are the `banned_names` option, kept on the web portal's Banned Names page.
- `colors_file=<string>`: Accepted but ignored. Colour names come from the colors.json file beside the server.
- `ssl_private_key_file=<string>`, `ssl_certificate_file=<string>`, `ssl_ca_file=<string>` and `ssl_ca_dir=<string>`: Accepted but ignored. TLS telnet uses the certificate the connection server is given where it is deployed.
