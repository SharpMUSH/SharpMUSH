# Sharpie production asset provenance

The immutable source sheets in `../sources/` are the user-supplied approvals from 2026-10-16. Active artwork is sheet 1 / 1 (primary), sheet 2 / 7 (smile), and sheet 2 / 8 (minimal sitting cat). The independent rounded face from sheet 1 / 8 was rejected and removed from the delivered asset set. Its appearance remains only in the original source sheet.

The final circular badge is **derived directly from the primary**, using the same complete paths at a uniform scale inside a white circle. It has no separate face, eyes, M or mouth drawing. The full ears remain inside the circle. The final set is `sharpie-primary`, `sharpie-primary-badge`, `sharpie-smile`, and `sharpie-minimal`.

## Native vector refinement

The final masters replace the initial threshold-based traces with explicit, manually refined SVG Bézier paths, polygons and ellipses. `reconstruct.py` is the editable coordinate source for every final path. The original approved geometry informed the reconstruction; it is not represented as pixel-identical extraction or recovered original vector authorship.

Corrections made per mark:

- **Primary:** rebuilt smooth outer-ear bows and cheek curves; retained the deliberate ear/fur points; regularized the forehead M; used mirrored ear/eye details, clean crescent irises and elliptical catchlights; merged adjacent mint cheek pieces into single contours; rebuilt the nose/stem/two-lobe smile. White chin and outer silhouette share identical lower curves, removing the earlier horizontal and antialias seams.
- **Primary badge:** removed the rejected independent rounded face completely; reused the exact full primary group inside a true white circle, with ample ear clearance. Chin has no dark hairline on the white backing.
- **Smile:** rebuilt the outer silhouette and heart-shaped white cheek/chin region; paired the inner ears and eye-smile curves by reflection; replaced noisy nose shoulders and irregular forehead vertices with deliberate curves and polygon coordinates.
- **Minimal:** rebuilt smooth head/body/tail contours, inner ears, white face, paws and tail accent; replaced threshold-derived eye boundaries with clean ellipses and regularized the M and nose.

All accent/detail groups are clipped to their intended silhouette. Intentional ear tips, fur points, M vertices and the tail lightning notch remain sharp. No automatic aggressive path simplification was used. The masters contain no embedded raster images, linked assets, scripts, fonts or external resources.

## Palette and measurement contract

Only three authored fill colors are used: ink `#17333E`, mint `#00DDB3`, and pure white `#FFFFFF`. Former cream fills and eye glints use the same pure white.

Every SVG has `viewBox="0 0 512 512"`. The major geometric dimension is normalized to 448 units, centered within the canvas. `measurements.json` records:

- original source selections and crop coordinates;
- actual alpha bounds measured from a 2048px render, expressed in 512-unit coordinates;
- Inkscape geometric bounding boxes for the forehead M, eye shapes and nose/mouth where present;
- exact authored nose-stem anchors for primary and derived badge;
- SVG hashes and construction notes.

Eye bounding-box centers are geometric feature centers, not inferred gaze/pupil centers. The nose-stem anchor is a designed path coordinate. Rendered alpha bounds include renderer antialiasing and may differ by fractions of a unit from exact vector bounds. Minimum-size and clear-space recommendations remain proposed design rules, not empirically certified readability thresholds.

## Validation and reproducibility

From the repository root, create `work/sharpie-production`, set `TMPDIR`, `TMP` and `TEMP` to that directory, then run:

```sh
python assets/branding/sharpie/approved/provenance/reconstruct.py
python assets/branding/sharpie/approved/provenance/edge_review.py
```

Requires Python/Pillow and Inkscape. No tracing package or original temporary attachment is needed. The source script regenerates SVGs, transparent 1024px PNGs, measured feature bounds, hashes and the dual-background preview. The review script validates SVG structure, unique IDs, palette and transparent corners, then creates native 4x light/dark render proofs and enlarged edge crops.

All four marks were rendered at 2048px and visually reviewed on light gray and dark backgrounds. The contour review covered ears, forehead marks, eyes, cheek tips, chin joins, body/paws and tail. Proofs are retained in `edge-review/`. These checks establish the delivered geometry and its rendering; very small favicon sizes, print processes, embroidery and future animation rigs still require medium-specific optical checks.
