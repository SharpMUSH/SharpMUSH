# Approved Sharpie onboarding icons

`sharpie-minimal.svg` is an unchanged copy of the owner-approved minimal mascot
from `assets/branding/sharpie/approved/svg/sharpie-minimal.svg` in the branding
workspace. It uses ink #17333E, mint #00DDB3 and white #FFFFFF, with native vector
transparency. It is a neutral fallback, not a genre-specific inventory approval.

New generation candidates must pass independent review and owner approval before
being shipped or added to SharpieArtCatalog. Preserve the forehead M and do not
replace this file with an unreviewed candidate.

`sharpie-primary.svg` and `sharpie-smile.svg` are also unchanged copies from
the same owner-approved SVG directory. Primary welcomes players before a journey;
minimal accompanies active lessons; smile marks completion. Only one mascot is
shown in the welcome view at a time. All three retain the original transparent
background and three-color palette. Explicit administrator artwork overrides
and approved genre selections take precedence over these neutral defaults.

The approved inventory covers all six styles, 43 typed MSSP genres/subgenres, and 21
onboarding topics (5,418 entries). Genre `None` and subgenre `None` are distinct.
The catalog selects artwork by style, theme and topic, and respects reduced/no imagery.

The raster artwork ships as WebP, 512 px wide, quality 90, keeping the original transparency and
aspect ratio. The portal never draws one wider than 16rem (256 CSS px), so 512 px covers a 2x
screen. Convert a new image the same way and give its `SharpieArtCatalog` entry the converted
width and height. Full-size PNG masters for the initial batch are preserved in repository history at
commit `1d9396ef`; the remaining batch and corrected Fantasy Chibi world master
are preserved at `629e97cee`. Compression does not replace those reviewed masters.
Resize RGBA with Lanczos, encode WebP at quality 90, and verify that the decoded
alpha channel exactly matches the resized source. Update catalog dimensions to
the encoded size; do not crop or stretch the artwork.
