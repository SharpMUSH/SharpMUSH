<!-- help-article
{
  "corpus": "help",
  "id": "config-parameters-command",
  "lookup": "@config parameters",
  "aliases": [],
  "sections": [
    {"id": "values", "heading": "Option values", "lookup": "@config parameters values"}
  ]
}
-->
# @config parameters

Many of the mush's run-time options can be set from the game by wizards, using `@config/set <option>=<new value>`. Those that can be set with visible changes are listed below, grouped by category. See `help @config <category>` for details on each.

Categories: [@config alias], [@config attribs], [@config chat], [@config cmds], [@config cosmetic], [@config costs], [@config db], [@config debug], [@config dump], [@config file], [@config flags], [@config funcs], [@config limits], [@config log], [@config net], [@config onboarding], [@config textfile], [@config tiny], [@config wiki].

The categories and groups are the same as those `@config` lists.

- [@config parameters values]: the value types options take, and list-valued options

## Option values

Values must be of the listed type for each option. They include: *<number>*, *<dbref>*, *<boolean>* (Yes/No), *<time>*, or *<string>*.

Options which take a *<time>* accept a plain number of seconds, such as `5400`; forms like `1h30m` are refused.

*<dbref>* options can be given with or without the leading '#', so '1' and '#1' are the same.

A few options map names to lists of words, such as `command_aliases`, `command_restrictions`, `sitelock_rules` and `mssp`. `@config <category>` and `@config <option>` show one entry per line, the name followed by its words.
