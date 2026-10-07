<!-- This aggregate has been migrated to semantic article files in this directory.
See docs/guides/help-migration.json for the old-to-new lookup map. -->

# IMAGE
# Image Attributes
# IMAGE\`BANNER
# IMAGE\`ALT
# IMAGE\`FOCAL

SharpMUSH seeds four standard attributes that name a picture for an object. The engine never reads them; the web portal and the bundled softcode do -- a player's portrait on `look`, a room's banner on the Play page, the preview of where an exit leads, a thing's thumbnail -- and any game that wants a picture somewhere reads the same four names rather than inventing its own.

- `IMAGE`             A URL. Character portrait, room banner, exit-destination preview, thing thumbnail.
- ``IMAGE`BANNER``     A URL for wide art: a character's profile banner, a room's Play-page banner. A room without one falls back to `IMAGE`; a character's profile does not, since its `IMAGE` is the avatar drawn beside the banner.
- ``IMAGE`ALT``        The alternative text a screen reader or a text client gets. Softcode falls back to the object's name.
- ``IMAGE`FOCAL``      `x y`, each 0 to 1, the point that stays in view when the picture is cropped (`0.5 0.5` is the centre). Optional.

All four are seeded `no_command`, `visual`, `prefixmatch` and `public`, so the owner sets them with `&` and anyone may `get()` them:

```sharp
> &IMAGE me=/assets/chars/tomas.jpg
> &IMAGE`ALT me=Tomas Reyes, at the harbour rail
> &IMAGE`BANNER here=/assets/rooms/lower-docks-wide.jpg
> &IMAGE`FOCAL here=0.5 0.6
```

There are no width or height attributes: every portal surface that shows a picture is a fixed-size box the picture is cropped into, so dimensions never affect layout. Only site-relative (`/...`) and `https:` URLs are rendered; anything else falls back to the no-image tile.

For players the portal's gallery will keep `IMAGE`, ``IMAGE`BANNER`` and ``IMAGE`ALT`` in step with its avatar and banner entries (set from the gallery, or with Change avatar and Change banner on the profile page), so a hand-set value on a player will be overwritten by the next gallery change. Rooms, things and exits are only ever set by hand.

The default flags apply when an attribute is created. An `IMAGE` a game set before these entries existed keeps the flags it was created with -- clear it and set it again to pick up `visual` and `public`. The bundled room-contents and profile-handler packages publish an image attribute only while it is `visual`, so clearing that flag (`@set me/IMAGE=!visual`) keeps a picture off the portal.

::: seealso
- [attribute flags access and matching]
:::

