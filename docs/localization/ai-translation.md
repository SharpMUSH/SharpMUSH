# Translating with an LLM

An LLM can draft all fifteen locales in an afternoon. That changes what the hard
part is: not producing strings, but knowing which of the ~16,000 produced strings
are wrong. This document is about the second problem.

LLMs fail at resource translation in a small number of predictable ways, and
almost all of them are mechanically detectable:

| Failure | Caught by |
|---|---|
| Drops or renames a placeholder | `validate_resx.py` gate 2, `merge_translations.py` pre-flight |
| Invents plural categories the locale doesn't have | `validate_resx.py` gate 3 |
| Silently returns English for a string it found hard | `validate_resx.py` advisory 5 |
| Translates a term of art (`dbref`, `softcode`) | glossary below + spot check |
| Translates a machine value (a slug, an enum name, a route) | glossary + `validate_resx.py` gate 1 |
| Renders a string 3× longer than English | `validate_resx.py` advisory 6 |
| Loses XML escaping (`&amp;`, `&lt;`) | `merge_translations.py` re-parses the file |
| Inconsistent terminology across batches | glossary in every prompt |

What it does **not** catch is register, idiom and outright mistranslation. Budget
a native spot-check of the ~400 player-facing keys per locale; the ~700 staff keys
can ship unreviewed and be corrected on report. See the surface breakdown from
`extract_untranslated.py --stats`.

## Before you translate anything

Both of these are prerequisites, not polish. An LLM cannot fix either, because
neither is a prose problem:

1. **Convert count-bearing values to ICU plurals** and collapse the one/many key
   pairs. See [`plural-forms.md`](plural-forms.md). Russian, Polish and Croatian
   need three or four categories; Chinese needs one.
2. **Restructure the thirteen values that put a placeholder inside a
   prepositional phrase** (`"Scene in {0}"`). Also in
   [`plural-forms.md`](plural-forms.md).

`python3 tools/i18n/validate_resx.py` fails until step 1 is done, which is the
intended gate.

## The loop

```bash
# 1. See the size of the job and the surface split
python3 tools/i18n/extract_untranslated.py ru --stats

# 2. Emit batches (40 strings is a good size — small enough that the model keeps
#    the glossary in view, large enough to stay consistent within a batch)
python3 tools/i18n/extract_untranslated.py ru --batch-size 40 --out /tmp/i18n/ru

# 3. Translate each batch with the prompt below, saving replies alongside

# 4. Splice back. Refuses to write anything at all if any string fails pre-flight
python3 tools/i18n/merge_translations.py ru /tmp/i18n/ru/*.reply.json

# 5. Full gate set
python3 tools/i18n/validate_resx.py --locale ru
```

Do the player-facing surfaces first so a partially-done locale is still useful:

```bash
python3 tools/i18n/extract_untranslated.py ru --surface "player-facing" \
    --batch-size 40 --out /tmp/i18n/ru-player
```

Each batch file already carries, per string: the key, the English value, the resx
`<comment>` if present, which portal surface it belongs to, and the exact
placeholder names to preserve. Stating the placeholders per-string is deliberate —
it is markedly more reliable than saying "preserve placeholders" once in a system
prompt.

## Prompt

Use this verbatim, substituting the language. Keep the glossary in every batch.

> You are translating UI strings for SharpMUSH, a text-based multiplayer
> role-playing (MUSH) server with a web portal. Translate from English into
> **Russian**.
>
> Input is JSON. Return the same JSON with a `"translated"` field added to each
> string. Add `"comment"` only where a translator's note is genuinely useful.
> Return nothing but the JSON.
>
> Rules:
>
> 1. **Preserve every placeholder exactly** — each string lists its
>    `placeholders`. `{0}`, `{count}` and `#` must appear in your output with the
>    same names. Never reorder them if the target grammar permits the English
>    order; reorder freely if it does not.
> 2. **ICU MessageFormat**: if the English value is
>    `{count, plural, one {...} other {...}}`, produce the categories Russian
>    actually needs — `one`, `few`, `many`, `other` — not a copy of English's two.
>    Keep the `#` symbol where the number goes.
> 3. **Do not translate the terms in the glossary below.** They are MUSH terms of
>    art, product names or machine identifiers. Leave them exactly as written,
>    including case, and inflect the surrounding sentence around them.
> 4. **Do not translate anything that looks like code**: `@command` names,
>    `attribute\`names`, routes like `/apps/{slug}`, file names like
>    `package.yaml`, CSS or icon identifiers.
> 5. **Register**: the portal addresses players directly and informally but not
>    jokily. Use the polite/formal second person where the language distinguishes
>    (Russian вы, French vous, German Sie). Be consistent across the whole batch.
> 6. **Length**: these are UI labels in fixed-width chrome. Prefer the shorter of
>    two accurate renderings. If a natural translation is more than twice the
>    English length, add a `"comment"` saying so.
> 7. If a string is genuinely untranslatable or you cannot tell what it means from
>    the `context` and `surface` fields, set `"translated"` to the English text and
>    add a `"comment"` explaining why. **Do not guess silently** — a flagged
>    fallback is useful, an invented translation is not.
>
> Glossary — do not translate:
>
> ```
> SharpMUSH  MUSH  MU*  PennMUSH        product and genre names
> dbref  softcode  MUSHcode            MUSH terms of art
> slug  namespace  attribute            used as identifiers in the UI
> Guest Player Builder Royalty Wizard God   privilege levels; also API values
> wiki                                 conventionally untranslated in most locales
> Markdown  JSON  YAML  package.yaml   formats
> telnet  WebSocket  SignalR  NATS     protocols
> ```

Adjust the glossary's last line per language: `wiki` is normally kept, but
`Markdown` and `telnet` are sometimes localized in Chinese and Russian — decide
once per locale and note it, rather than letting it vary batch to batch.

## Locale-specific notes

**Russian, Polish, Croatian** — three or four plural categories. Also: these
languages inflect nouns for case, so any remaining `"preposition {0}"` shape will
read wrong. That is why the restructuring step is a prerequisite.

**Chinese (Simplified)** — one plural category; a `{count, plural, other {...}}`
with a single branch is correct and expected, not a shortcut. Two rendering traps
that are CSS problems rather than translation ones: Chinese has no word spaces, so
`word-break`/`overflow-wrap` and the `text-overflow: ellipsis` on nav chips behave
differently than tested; and the theme pins JetBrains Mono in places, where CJK
glyphs fall back per-glyph and look broken. Expect to fix both when `zh-Hans`
first renders.

**German** — compounds run 30–50% longer than English. Advisory 6 will fire a lot;
these are real, and the nav labels, chips and stat tiles are where they matter.

**Portuguese** — `pt-BR` only. Penn's `pt_PT` file was an empty stub; the active
community is Brazilian.

**Norwegian** — tag it `nb` (Bokmål), not `no`, which is a macrolanguage.

## Do not ship silently machine-translated strings as reviewed

Mark them. The simplest mechanism that survives this toolchain: put
`MT` in the resx `<comment>` for any value no human has read, and grep for it when
someone offers to review a locale. It costs nothing and it means "we support
Russian" never quietly means "an LLM had a go at Russian".
