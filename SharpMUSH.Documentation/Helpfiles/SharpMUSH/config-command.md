<!-- help-article
{
  "corpus": "help",
  "id": "config-command",
  "lookup": "@config",
  "aliases": [],
  "sections": [
    {"id": "listing", "heading": "Listing options", "lookup": "@config listing"},
    {"id": "set", "heading": "Changing an option", "lookup": "@config set", "aliases": ["@config/set"]},
    {"id": "stored", "heading": "The stored configuration", "lookup": "@config stored", "aliases": ["@config/save"]}
  ]
}
-->
# @config

`@config`<br>
`@config [<category>|<option>]`<br>
`@config/set <option>=<value>`<br>
`@config/save <option>=<value>`

With no arguments, @config lists the categories of configuration options for the MUSH. With an argument, @config lists the options in the given `<category>`, or the options whose names match `<option>`; see [@config listing].

When exactly one option matches, `@config` outputs its value, as `config()` returns it; otherwise it outputs nothing.

Only God can see the SQL credentials (sql_username, sql_database). The SQL password is not a configuration option: it is the `sql.password` secret, which nobody can see. See [@secret].

For information about parameters, see [@config parameters].

`/list` and `/lowercase` are accepted, as in PennMUSH, and list the same way as `@config` alone.

- [@config listing]: what a listing shows
- [@config set]: which values an option takes, and what cannot be set
- [@config stored]: where a change is kept, and options the game does not read

## Listing options

`@config <category>`<br>
`@config <option>`

`@config <category>` lists the options in that category. `@config <option>` lists every option whose name starts with `<option>`, or, if none does, every option whose name contains it, and links the categories they are in. Each option's name, which runs `@config <name>` when clicked, stands beside its value; an option that takes one of a fixed set lists the set under it.

```sharp
> @config image
+=================< Configuration options matching 'image' >=================+
| image_hosts:     any                                                       |
|                  One of: any, allow, block, off                            |
| image_host_list:                                                           |
+============================================================================+
| Cosmetic * All categories                                                  |
+============================================================================+
```

## Changing an option

`@config/set <option>=<value>`

The wizard-only `/set` switch changes the value of `<option>` to `<value>`. Booleans take yes/no, true/false or 1/0; numbers may be written with a leading `#`; an object option takes -1 for none. A value that does not fit the option, or that the configuration's own checks reject, is refused and nothing changes. Options naming files (the File and Message categories), the SQL credentials, and list-valued options (banned names, sitelock rules, restrictions; see [@sitelock]) cannot be set this way.

## The stored configuration

`@config/save <option>=<value>`

SharpMUSH keeps one stored configuration, which the web portal's configuration page also edits, and the game reads its options from it. So every `/set` is stored and lasts across restarts. God may also use `/save`, which does the same and says so; there is no mush.cnf to write back to. A few options are not read by the game at all (the listening addresses and ports belong to the connection server's own configuration), so setting them has no effect.
