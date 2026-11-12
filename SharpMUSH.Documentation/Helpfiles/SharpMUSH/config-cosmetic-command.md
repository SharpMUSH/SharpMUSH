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
    {"id": "pictures", "heading": "Pictures", "lookup": "@config cosmetic pictures"},
    {"id": "portal", "heading": "The web portal", "lookup": "@config cosmetic portal"},
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
- [@config cosmetic pictures]: which pictures may be shown
- [@config cosmetic portal]: the portal's logo, tab icon and Home header
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

## Pictures

- `image_hosts=<string>`: Which pictures [image()] and [figure()] may show: `any` (every host), `allow` (only the hosts on image_host_list), `block` (every host but those) or `off` (none). The game's own pictures, given as a relative address, are shown unless it is `off`. A refused picture shows its text art or description.
- `image_host_list=<string>`: The hosts image_hosts allows or blocks, separated by spaces, such as `i.imgur.com *.example.com`. A `*.` entry covers every subdomain.

## The web portal

These options set the game's look in the web portal. The identity step of Prepare for players in the portal sets them too, with a preview of Home's header.

- `portal_logo=<string>`: The picture at the top left of the web portal: a media upload (`/api/wiki-assets/...`) or an http or https address. It is shown square, 26 pixels across. Empty shows the SharpMUSH logo.
- `portal_favicon=<string>`: The web portal's browser tab icon, given the same way. Empty uses portal_logo, or the SharpMUSH logo when that is empty too. A browser keeps a tab icon for a while, so a change can take a reload to show.
- `portal_banner=<string>`: A wide picture behind the game's name on the portal's Home, wiki and sign-in pages, given the same way. It is cropped to fit, about four times as wide as it is tall, and link previews show it too. Empty shows none.
- `portal_tagline=<string>`: One line of at most 120 characters, shown under the game's name on the portal and in link previews.
- `portal_description=<string>`: Two or three sentences, at most 300 characters, about the game, for the portal's Home and sign-in pages and link previews. While it is empty, link previews use onboarding_introduction.
- `portal_side_art=<string>`: A picture at the right of the game's name on the portal's Home page, given the same way, such as the game's mascot or crest. It is shown over the banner when there is one. Empty shows Sharpie instead, as portal_sharpie says.
- `portal_sharpie=<boolean>`: Whether Home shows Sharpie, the guide's mascot drawn for the game's genre, at the right of the game's name when portal_side_art and portal_banner are empty.

## Layouts

`@config <option>` lists the values an option with a fixed set takes; see [@config].

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
