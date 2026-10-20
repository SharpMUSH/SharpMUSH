<!-- help-article
{
  "corpus": "help",
  "id": "monikers",
  "lookup": "monikers",
  "aliases": [],
  "sections": [
    {
      "id": "display-configuration",
      "heading": "Display configuration",
      "lookup": "monikers display configuration"
    }
  ],
  "redirects": {
    "MONIKERS2": "monikers display configuration"
  }
}
-->
# Monikers

Monikers are ansi templates which allow objects to have colored names. They can be set via the `@moniker` command, and can always be viewed via the `moniker()` function and %k substitution. Monikers may also be used automatically by MUSH commands, depending on how the "monikers" `@config` option is set.

By default, anyone can use the `@moniker` command to set a moniker for themselves or their objects. However, whether monikers are displayed automatically is controlled via the "monikers" `@config` option.

## Display configuration

The "monikers" `@config` option is yes or no. When it is yes, the speaker's moniker colors their name in say, pose and semipose. Other messages (channels, movement, look, examine, WHO) show plain names.

When it is no, monikers are not displayed automatically, and are only available via softcode (`moniker()` and %k).

::: seealso
- [@moniker]
- [MONIKER()]
:::
