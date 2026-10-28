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
    },
    {"id": "pictures", "heading": "Pictures and the portal", "lookup": "@config cosmetic pictures"},
    {"id": "layouts", "heading": "Layouts", "lookup": "@config cosmetic layouts"}
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

- [@config cosmetic page and flag display]: page aliases, examine, wall prefixes and names
- [@config cosmetic pictures]: which pictures may be shown, and the portal's logo and tab icon
- [@config cosmetic layouts]: the border and colour theme of layouts that name none

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
- `only_ascii_in_names=<boolean>`: Names are ascii-only or are extended characters permitted?

## Pictures and the portal

- `image_hosts=<string>`: Which pictures [image()] and [figure()] may show: `any` (every host), `allow` (only the hosts on image_host_list), `block` (every host but those) or `off` (none). The game's own pictures, given as a relative address, are shown unless it is `off`. A refused picture shows its text art or description.
- `image_host_list=<string>`: The hosts image_hosts allows or blocks, separated by spaces, such as `i.imgur.com *.example.com`. A `*.` entry covers every subdomain.
- `portal_logo=<string>`: The picture at the top left of the web portal: a media upload (`/api/wiki-assets/...`) or an http or https address. It is shown square, 26 pixels across. Empty shows the SharpMUSH logo.
- `portal_favicon=<string>`: The web portal's browser tab icon, given the same way. Empty uses portal_logo, or the SharpMUSH logo when that is empty too. A browser keeps a tab icon for a while, so a change can take a reload to show.

## Layouts

`@config <option>` lists the values an option with a fixed set takes.

- `layout_border=<style>`: The border box() and rule() draw when they name none. Unset, it is `double`. One of:
  - `double` - double lines
  - `single` - light lines
  - `heavy` - thick lines
  - `rounded` - light lines with rounded corners
  - `mush` - ASCII, like `ascii` but with edges of `=`
  - `ascii` - ASCII, with edges of `-`, sides of `|` and corners of `+`
  - `none` - no frame, only the title and the padded body
- `layout_theme=<theme>`: The colour theme of layouts that name none: a name themes() lists, such as `nord`, or a theme as JSON, such as `{"seed":"#7aa2f7"}`. Unset, it is `sharpmush`; `none` gives no colour. See [layout themes].

A client without Unicode is sent the Unicode styles in ASCII; see [layout borders ascii].

```sharp
> @config layout_border
 layout_border                            double
   One of: double, single, heavy, rounded, mush, ascii, none
```
