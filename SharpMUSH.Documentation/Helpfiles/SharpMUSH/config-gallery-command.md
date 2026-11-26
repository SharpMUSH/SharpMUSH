<!-- help-article
{
  "corpus": "help",
  "id": "config-gallery-command",
  "lookup": "@config gallery",
  "aliases": [],
  "sections": []
}
-->
# @config gallery

How staff moderate the character galleries on the web portal. Every option is off by default, which leaves a gallery to its owner. Staff here are those who hold `media.admin` (see [roles]). The image count is "max_gallery_images" (see [@config limits]).

- `gallery_consent_text=<string>`: Terms an uploader accepts before every upload, such as a promise that they made the picture or may use it. The gallery keeps who accepted them, when, and the words they read with each image, and shows that record to staff. Empty asks for nothing.
- `max_gallery_megabytes=<number>`: How many megabytes one character's images may take together. An upload past it is refused. `0`, the default, sets no limit.
- `gallery_limit_skips_official=<boolean>`: Official art counts toward neither "max_gallery_images" nor "max_gallery_megabytes". Official art is a portrait staff approved, or an image staff marked official.
- `gallery_portrait_approval=<boolean>`: A player's new portrait waits for staff, and the current one stays until they approve it. Staff can also clear a portrait, and the profile then shows the default.
- `gallery_staff_only=<boolean>`: Only staff add, change or remove gallery images. Players can still view and report them.

Example: asking every uploader to confirm they may share a picture.
```sharp
> @config/set gallery_consent_text=I made this picture, or its artist lets me use it here.
```

::: seealso
- [gallery moderation]
- [gallery portraits]
:::
