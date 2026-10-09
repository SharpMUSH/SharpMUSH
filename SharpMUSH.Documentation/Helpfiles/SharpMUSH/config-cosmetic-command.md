<!-- help-article
{
  "corpus": "help",
  "id": "config-cosmetic-command",
  "lookup": "@config cosmetic",
  "aliases": [],
  "sections": [
    {
      "id": "page-and-flag-display",
      "heading": "Page and flag display",
      "lookup": "@config cosmetic page and flag display"
    }
  ],
  "redirects": {
    "@config cosmetic2": "@config cosmetic page and flag display"
  }
}
-->
# @config cosmetic

These are cosmetic options of various sorts.

- `money_singular=<string>`: What is one penny called?
- `money_plural=<string>`: What are many pennies called?
- `player_name_spaces=<boolean>`: Can player names have spaces in them?
- `ansi_names=<boolean>`: Are names in look hilighted?
- `monikers=<list>`: Where should @monikers be displayed? See [monikers]
- `float_precision=<numbers>`: How many digits after the decimal point in floating point numbers are kept when formatting the result of a floating point function?
- `comma_exit_list=<boolean>`: Do exits show up like North, East, and West or as North East West?
- `count_all=<boolean>`: Does the count of connected players in WHO include hidden connections for mortals?
- `ascii_translations=<pairs>`: What a client without Unicode is sent for a character it cannot show, such as a middle dot sent as an asterisk. See [ascii translations]

## Page and flag display

More cosmetic options.

- `page_aliases=<boolean>`: Are aliases included in page listings? For example, Foo(F) pages: Blah
- `flags_on_examine=<boolean>`: Are flag names included when examining objects?
- `ex_public_attribs=<boolean>`: Show visual attributes when examining objects you don't control?
- `wizwall_prefix=<string>`: Prefix for @wizwall messages.
- `rwall_prefix=<string>`: Prefix for @rwall messages.
- `wall_prefix=<string>`: Prefix for @wall messages.
- `announce_connects=<boolean>`: Should (dis)connects be announced to non-HEAR_CONNECT players and to channels?
- `chat_strip_quote=<boolean>`: Does +chan "foo strip the "?
- `newline_one_char=<boolean>`: Is strlen(%r) equal to 1?
- `only_ascii_in_names=<boolean>`: Names are ascii-only or are extended characters permitted?
