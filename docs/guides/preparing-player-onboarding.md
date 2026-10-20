# Preparing player onboarding

Administrators with configuration permission can open `/admin/onboarding` from
the dashboard. First Setup also offers an identity step after import. Review the
prefilled imported values; save explicitly or defer the step. The unsaved preview
updates the welcome heading and introduction as you type. Artwork follows the
selected style and genre when a reviewed illustration is available; otherwise
the preview identifies the default Sharpie fallback.

Set the game name, short introduction, one MSSP genre, one subgenre and preferred
Sharpie style. Both category dropdowns use the shared MSSP catalog; choose `None`
for a category that does not apply. The server rejects multiple or unlisted
values submitted through this form. Existing unsupported imported values remain
visible until you select a replacement. Genre changes preserve unrelated MSSP entries.
The page and its settings API require the same `config.admin` permission as the
configuration editor. Welcome, rules and character
guide references use `namespace/slug`, for example `main/welcome`.

A configured wiki link is shown publicly only when the wiki reader is enabled and
an anonymous visitor can see the page. Do not use the introduction for secrets.

Work through the readiness checklist: identity, joining, rules, capabilities,
ordinary-player walkthrough and invitations. Mark a step verified only after
checking it. Deferring a step records that choice; it does not enable features,
install packages, publish pages or invite anybody.

Use the player preview to inspect guide wording without changing your progress.
Then test with an ordinary player account: administrator privileges can conceal
missing permissions and unavailable routes. Check sign-in, character creation or
claim, entering play, help and a harmless command. Check narrow screens and
keyboard navigation as well.

Only reviewed, shipped art is eligible for the client catalog. Unknown genres
never become image paths. New inventory candidates remain separate from approved
assets, and the text journey remains usable without artwork.
