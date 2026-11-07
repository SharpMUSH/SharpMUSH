# Character applications

The `chargen` package keeps applications as drafts on characters, records each submitted round,
and lets reviewers comment, return or approve them. The default Background field uses chapters
stored in `BG1`, `BG2`, and so on. `chargen-app` provides the portal form; `chargen-help` and
`chargen-config` add the help topics and settings to their respective systems.

## Upgrading older Background drafts

Before chargen 2.3, the default Background draft was stored in `BACKGROUND`. Versions 2.3–2.7
changed the default mapping to `BG*` without moving that draft. The old text was not deleted,
but it disappeared from the application and could fail required-field validation.

Starting with 2.7.1, a direct update from a version before 2.3 copies each registered applicant's
nonempty `BACKGROUND` to `BG1` when:

- the package lifecycle supplies the previous installed version;
- this update newly creates the default `BG*` mapping, rather than adopting a locally configured one; and
- the character has no `BG1` attribute, including an explicitly empty one.

Existing chapters and custom mappings are preserved. The original `BACKGROUND` and submitted
rounds are left intact. Comparisons with an older single-attribute round use the whole Background
field, so its original text does not appear as an empty chapter. The migration does not run on fresh installation, ordinary startup,
or another update from a chaptered version, so clearing a recovered chapter keeps it cleared.

### Games already updated to 2.3–2.7

These games require staff review. A hidden legacy draft and a chapter intentionally cleared
by its author can have identical stored attributes, so the package does not guess. The same
manual procedure applies when upgrading on a server that does not supply lifecycle versions,
or if the lifecycle cannot finish copying an applicant's text.

1. Check `+apps/field background` and confirm the mapping is `BG*`.
2. Inspect the character's `BACKGROUND` and existing `BG*` attributes. Confirm with the author
   which text should be restored. Do not overwrite an existing or intentionally empty chapter.
3. If chapter 1 is absent and the author wants the old text restored, copy the attribute as
   staff, using the character's actual dbref in place of `#123`:

   ```
   @cpattr #123/BACKGROUND=#123/BG1
   @set #123/BG1=wizard
   ```

4. Have the author check `+app` or the portal draft before submitting. Leave the original
   `BACKGROUND` in place until the recovery has been verified.

`@cpattr` copies stored text without evaluating it. Do not paste background text into a command
that evaluates it, and do not change the whole game's mapping to recover only one character.
