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

Fantasy minimalist v03 and Science Fiction minimalist v02 were explicitly approved for onboarding by the owner on 2026-10-20. Their approval is specific to these two images; other candidates remain excluded.

The raster artwork ships as WebP, 512 px wide, quality 90, keeping the original transparency and
aspect ratio. The portal never draws one wider than 16rem (256 CSS px), so 512 px covers a 2x
screen. Convert a new image the same way and give its `SharpieArtCatalog` entry the converted
width and height. The full-size PNG masters are in this repository's history at commit 1d9396ef.
