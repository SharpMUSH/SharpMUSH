<!-- help-article
{
  "corpus": "help",
  "id": "gallery",
  "lookup": "gallery",
  "aliases": ["galleries", "profile gallery"],
  "sections": [
    {
      "id": "gallery-warnings",
      "heading": "Warnings and tags",
      "lookup": "gallery warnings"
    },
    {
      "id": "gallery-softcode",
      "heading": "Reading a gallery in softcode",
      "lookup": "gallery softcode"
    },
    {
      "id": "gallery-reports",
      "heading": "Reports and new images",
      "lookup": "gallery reports"
    },
    {
      "id": "gallery-moderation",
      "heading": "Moderation",
      "lookup": "gallery moderation"
    },
    {
      "id": "gallery-portraits",
      "heading": "Portrait approval",
      "lookup": "gallery portraits"
    }
  ]
}
-->
# Gallery

Every character has a gallery of pictures on their web portal profile. Its owner adds images, puts them in order, gives each a caption, tags and a content warning, and chooses which one is the avatar and which the banner. The profile shows the avatar and two more; View all opens the gallery page at `/character/<name>/gallery`, which filters by tag.

Whoever may edit the character's biography may edit the gallery: the account that owns the character, or staff who control it (see [roles]). A game can moderate galleries instead (see [gallery moderation]). The portal takes PNG, JPEG, GIF and WebP files up to 10 MB, and checks the file's own bytes, not just the type the browser names. A gallery holds at most "max_gallery_images" images (50 unless the game changes it; see [@config limits]).

The avatar and the banner are copied into `IMAGE` and ``IMAGE`BANNER``, and the avatar's caption into ``IMAGE`ALT``, every time the gallery changes, so softcode and clients read the same picture (see [IMAGE]).

::: seealso
- [IMAGE]
- [event gallery]
- [@config gallery]
:::

## Warnings and tags

A content warning is a few words, such as `blood` or `spiders`. The portal covers the image with them until a viewer chooses Show image. A warned image is never the avatar or the banner, which show everywhere without a click: a warning moves the avatar to the first image without one.

Tags are single words or short phrases, lower case, up to 10 per image. The gallery page shows one button per tag and keeps only the images carrying it.

Limits: a caption up to 300 characters, a warning up to 100, a tag up to 32.

## Reading a gallery in softcode

The gallery is kept as JSON in the character's ``PROFILE`GALLERY`` attribute, one object per image in display order:

- `AssetId`, `FileName`, `Url`: the stored file and its site-relative address.
- `Caption`, `Warning`: text, or null.
- `Tags`: a list of words, or null.
- `Order`: the place, from 0.
- `IsIcon`, `IsBanner`: whether it is the avatar or the banner.
- `Official`: staff approved it as the portrait or marked it official art. Left out when false.
- `PortraitPending`: the owner asked for it to be the portrait, and it waits for staff. Left out when false.
- `Consent`: the terms the uploader accepted, as `Text`, `By` (their objid) and `At`. Left out when the game asked for none.

Change the gallery from the portal: a value set by hand is replaced by the next change made there.

Example: counting your images and reading the first one's caption.
```sharp
> think json_query(get(me/PROFILE`GALLERY),size)
3
> think json_query(get(me/PROFILE`GALLERY),extract,$\[0\].Caption)
At the harbour rail
```

## Reports and new images

Two events let the game decide what happens to the gallery's images (see [event gallery]):

- ``GALLERY`ADD`` runs after an image is added. Use it to tell staff about new pictures.
- ``GALLERY`REPORT`` runs when someone signed in to the portal reports an image. The portal offers Report only while the event handler has ``GALLERY`REPORT`` or a package's attribute under it. The bundled jobs package sets ``GALLERY`REPORT`JOBS``, which files a job in its Staff bucket.

Example: telling connected wizards about every report.
```sharp
> &GALLERY`REPORT #9=@wizwall/emit [name(%#)] reported an image of [name(%<objid>)]: %<reason> (%<url>)
```

## Moderation

Options in [@config gallery] let staff moderate galleries. Each is off by default. Staff here are those who hold `media.admin`: moderators, royalty and wizards unless the game changes the roles.

- Terms: with "gallery_consent_text" set, every upload first shows the terms and asks the uploader to accept them. The image keeps who accepted, when, and the words they read; staff see that record under Edit in the image viewer. An upload sent without accepting, or after the terms changed, is refused.
- Space: "max_gallery_megabytes" limits how much a character's images take together, beside the count in "max_gallery_images". With "gallery_limit_skips_official" on, official art counts toward neither.
- Portraits: "gallery_portrait_approval" makes a new portrait wait for staff (see [gallery portraits]).
- Lock: with "gallery_staff_only" on, only staff add, change or remove images, on any character. Players still view and report them.

Staff can mark any image official art, or unmark it, with Official art in the image viewer.

## Portrait approval

With "gallery_portrait_approval" on, a player's new avatar waits for staff, and the current one stays until they approve it. The portal's button reads Ask for portrait, and the owner can withdraw the request. Only the latest request waits.

Staff approve or decline requests at `/admin/portraits`, or in the image viewer. An approved image becomes the portrait and is marked official art. A declined one stays in the gallery. Staff can also clear a portrait: the profile shows the default until they approve another.

While this is on, no image becomes the avatar by being first, and removing the avatar leaves none.

``GALLERY`SUBMIT`` runs when a player asks, and ``GALLERY`PORTRAIT`` when staff decide (see [event portrait]).
