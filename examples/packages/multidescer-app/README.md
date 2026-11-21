# multidescer-app

Registers the [`multidescer`](../multidescer/) package as a portal page, **Descriptions**, at `/apps/descs`, under
Play. A signed-in player sees what they wear now as others see it, their pieces grouped by slot and their outfits,
and can:

- write a new piece in any slot, kept exactly as written or, ticked, as code that runs when someone looks;
- open a piece to wear it alone, put it on, take it off, change its name, slot and text, or delete it;
- open an outfit to wear it, put it on over what they wear, take it off, rename it, save what they wear now as it,
  or delete it;
- save what they wear now as a new outfit, take everything off, and set their short description.

Each button runs the same routine as `+desc`, `+wear` or `+remove`, so the rules are the same: slot limits, covered
slots, a hand-written description kept before it is replaced, and the same refusals, shown on the page.

Requires `multidescer` 1.1 or later. Owns no objects.
