# Sharpie guided journeys

Status: onboarding design approved by the user, 2026-10-19. Player onboarding
comes first; new-administrator onboarding is a separate, required companion
journey. The six artwork styles are approved; candidate production and independent visual QA run in parallel.

## Outcome

A person arriving at an established game can understand what it is, sign in,
create or claim a character, enter play, perform a useful action, and find help.
Experienced players can enter play immediately and learn only what is different.
New administrators can prepare that experience without learning every setting first.

Sharpie is a friendly guide to this game's interface. She is not a chatbot, a
player character, or a replacement for the game's own identity. Guidance is
authored, deterministic and sensitive to the features actually available.

## Decisions from the user

- Offer paths for Discord users, PennMUSH/other MUSH players, people from other
  roleplay environments, and people who prefer to figure things out themselves.
- Explain login, the client, sending commands and useful everyday commands.
- Use the admin-selected game genre to specialize Sharpie's artwork; the atlas
  includes anime, chibi, kemono, painterly and other representations.
- Prioritize players joining a configured game. Also improve First Setup and
  provide a new-admin onboarding journey.
- Keep Sharpie's guidance optional, resumable, accessible and unobtrusive.
- Generate a new per-genre artwork inventory with native transparent backgrounds
  and no baked-in text. Reduce the style count in collaboration with the user;
  the old atlas is reference material, not the production inventory.

The journey below is approved. The six artwork styles and production across all 42 genre/subgenre themes are approved.
Artwork is being generated in parallel and reviewed in batches.

## Refined delivery goal and acceptance gates

Deliver an optional, player-first journey that helps a newcomer join an established
game, choose or create a character, understand the client, practice safely and
perform a useful action. Preserve immediate independent exploration. Give MUSH
veterans a concise, verified explanation of what SharpMUSH adds. Provide a
separate administrator identity/setup and readiness checklist.

Completion requires:

- All four familiarity paths work end to end, including account/character handoff,
  dismissal, resumption, explicit anonymous-progress import and account isolation.
- Guidance reflects enabled features and actual command availability. Practice is
  local; inserting a command never sends it or replaces an existing draft.
- Desktop, narrow-screen, keyboard and light/dark journeys have actual runtime
  evidence and screenshots, including an ordinary player and an administrator.
- Automated persistence, authorization, conflict, routing, composition and UI
  regression checks pass. Independent code review findings are addressed.
- The fresh inventory contains 252 separate, text-free, native-transparent images:
  42 genre/subgenre themes in six approved styles. Each version has independent
  anatomy, prop, clipping, palette and light/dark edge QA. Failed candidates stay
  excluded; QA acceptance never substitutes for the owner's visual approval.
- Production resolves only approved artwork through a bounded catalog, with a
  neutral fallback and reduced/no-image options. Artwork review does not block
  implementation or testing of the functional journey.
- Supporting documentation, reviewable PRs and validation evidence are delivered.
  Merging and deployment require separate authorization.

Track functional delivery and artwork acceptance separately. Generated counts,
passing isolated tests and mock previews do not by themselves close the goal.

## Experience

### Entry and choice

On the game's home page, a compact “New here? Let Sharpie show you around” card
links to `/welcome`. Login offers the same optional help link. Neither opens a
modal automatically or delays login. The persistent Help menu offers “Getting
started with Sharpie” and “Change my guide”. A direct `/play` visit remains direct.

The welcome page leads with the game name, short setting introduction and its
rules/character-guide links where configured. Sharpie asks “What feels familiar?”

| Choice | First lesson | What gets abbreviated |
|---|---|---|
| I play on Discord | Your account, your character, and the shared world | Technical MUSH history |
| I know MUSHes | What this portal adds to familiar play | Basic roleplay and command explanations |
| I roleplay elsewhere | Places, characters, scenes, and how people take turns here | Software-specific analogies |
| Let me explore | Continue to the intended destination, with help available later | All proactive guidance |

The MUSH path also offers “Just show me what's new in SharpMUSH”. The other-RP
path offers optional examples for forum/play-by-post and tabletop players without
requiring another questionnaire. Players can change paths without losing progress.

Discord comparisons are bridges, not false equivalences: a room is a shared place,
not necessarily a persistent chat channel; a character is an identity inside this
game, not a separate Discord account; scene archives depend on game capabilities.

### Common journey, adapted explanations

1. **Find your place.** Explain this game's setting and how to find its rules.
   Reading guidance is available before login. Do not manufacture setting lore or
   require a click acknowledging that the player has read rules.
2. **Sign in.** Explain web accounts versus characters. Link to the real login
   form and show only enabled sign-in methods. Preserve the intended destination.
   Existing players can claim an existing character rather than create a duplicate.
3. **Choose who you are.** Explain create, claim, and select using the actual
   account roster. Character creation is distinct from any game-specific approval
   or character-sheet process; display the configured character guide when needed.
4. **Enter the world.** On Play, explain the current character and connection state,
   Story/Terminal views, Here/Exits, and where help lives. Adapt to mobile controls.
   Failed roster requests remain errors with Retry, never “you have no character”.
5. **Try one useful action.** Offer an explicitly labelled local practice example,
   then let the player insert `look` into Command mode and choose Send themselves.
   Typing a command in Say mode must not be taught as equivalent to executing it.
6. **Speak and act.** Explain Say, OOC, Emit and raw Command. Show `say Hello!`,
   `pose smiles.`, and `page Name=Hello` as examples with their audience explained.
   Never send example speech, private messages, movement or package commands for
   the player. The local practice preview is unmistakably not live game output.
7. **Find people who can help.** Show how to list channels, join a named channel,
   and ask a question. Explain that channel listeners can be outside the room.
8. **Continue your story.** Offer available next steps: game guide, finding people,
   scenes, communication and help. Completion is a modest “You're ready to explore”,
   not a full-screen celebration or a mandatory tour of every feature.

Each step has one main action and Back, Skip this step, and Stop this guide. The guide is a
player-opened side sheet on desktop and mobile, reached from the Play header.
It never consumes space above the terminal or opens automatically over play.
The sheet is modal while explicitly open: Escape or Back to game restores
focus to the opener without changing progress. Stop this guide dismisses the journey. Successful command insertion closes it without sending the
command or dismissing the journey; refusal keeps the explanation visible.
The full guide page remains available for longer reading.

The guide may point to a control only while that control exists and is visible.
Stable semantic anchors, not screen coordinates, identify controls. A missing or
customized surface gets a plain explanation/link, not a broken spotlight.

### Experienced-player route

Keep the first screen short: “Keep your commands. Use the browser when it helps.”
Explain account/character linking, Story versus Terminal, literal message modes
versus evaluated commands, shared wiki, and optional scenes/packages. Show only
features the current game exposes to this player. Do not claim complete PennMUSH
compatibility or promise that every game uses the same softcode.

The command card starts with `look`, `help`, `help say`, `inventory`, `who`,
`say`, `pose` and `page`, verified against engine definitions and runtime behavior.
Games may customize command access and output. Package commands such as `+help`,
`+scene` and dice commands are separate capability-gated entries, with documented
help links and actual installed versions as evidence. No package-name-only guesses.

## Genre and artwork

Select artwork by explicit admin override, then supported subgenre, supported genre,
then neutral Sharpie. The onboarding editor accepts one genre and one subgenre from the shared MSSP
catalog, with `None` for an absent category. Imported unsupported values remain
visible until explicitly replaced; unknown runtime metadata uses the neutral fallback.
Genre does not determine art style. Admins choose one coherent style from the
six approved styles: minimalist, chibi, anime (androgynous non-binary looks), kemono, pixel and comicbook; players can choose reduced imagery or no mascot.

The old atlas has 42 themes and 12 styles, 504 individual images. Use it as visual
reference while generating new, individually framed illustrations with native
alpha, no baked-in text, complete anatomy and complete silhouettes. Do not remove
the backgrounds from the rejected old cuts or upscale them for production. Record
provenance and dimensions; serve only selected assets. Review every new asset on
white, near-black and representative theme colors at its actual display size.
Reject halos, stray pixels, extra limbs, clipped ears/paws/tails and drifting mint.
Approved transparent brand SVGs remain suitable for compact UI guidance. Preserve
ink/mint/white identity and the canonical M.

The same guide keeps the same representation across steps. Genre art can head the
journey and chapter cards; compact instructions do not need a new illustration each.
Every selected style needs a neutral fallback. Until that style has an approved shipped asset, use the owner-approved neutral minimalist SVG rather than leaving Sharpie absent. The lesson-card host remains visible after the welcome header scrolls away, including in the player-opened Play guide sheet. Reduced imagery uses a smaller host; no-art mode omits both header and card illustrations. Loading errors never block guidance.
Illustrations should explain the current activity through Sharpie's interaction
with a genre-specific object, rather than repeat a labeled mascot badge. The first
Fantasy pilot covers welcoming through an open portal, navigating with a map,
and observing with a magnifier or telescope for the `look` lesson. Keep the same
character and chosen style across these scenes. The visible “With Sharpie” label
is omitted from lesson headers; stop remains an icon control with a localized
accessible name and tooltip. Genre metadata alone must never promote unreviewed
candidate art into the shipped catalog.

No image generation occurs during a user's visit.

Inventory check (2026-10-19): all 504 PNGs decode, with exactly 42 images for each
of the 12 styles. The set occupies 64,609,741 bytes. Native widths are 218–355 px
and heights 208–333 px; none is a high-resolution hero asset. This reinforces the
decision to generate new assets rather than reuse the cuts. New assets must have
sufficient native resolution for their intended display size and device density,
with `object-fit: contain` and uncropped bounds. Do not include the whole atlas in initial download
or a service-worker precache. File integrity is not anatomy or visual approval;
the production selection still needs per-image review.

## Administrator journey

First Setup currently includes claim/sign-in, import, handlers and packages. Keep
that lifecycle and its permissions intact. Add a concise identity step after the
claim/import has resolved: game name, short public introduction, genre/subgenre,
and a preview of Sharpie's suggested style. Saving is explicit and resumable.
An imported game's existing values are prefilled and never silently overwritten.

After existing setup completes, offer a separate “Prepare for your first players”
checklist available from admin overview:

1. **Identity and welcome:** name, description, genre, logo/theme and guide style.
2. **Staff character:** keep #1 (God) for emergencies; create a separate character,
   set it WIZARD as #1, and give other staff the narrowest role that fits. The step
   notes when #1 is the active character.
3. **Joining:** review enabled registration/sign-in methods, create/claim paths,
   guest availability if supported, and game-specific character requirements.
4. **Starting room:** describe the `player_start` room and its exits; keep the
   master room private.
5. **Rules and setting:** select existing welcome/rules/character-guide wiki pages;
   offer the existing starter-wiki mechanism only through its explicit action.
6. **Channels:** a public channel for questions and a staff-only one; a new world
   has none, and the player guide's channel lesson relies on one existing.
7. **Connect screen:** the connect screen and messages of the day.
8. **Useful capabilities:** explain installed packages and their player-visible
   features, linking to the existing package review/apply workflow. No hidden install.
9. **Backups:** one `@backup`, a schedule, and a copy off the machine.
10. **Walk the player path:** preview each familiarity route with the current config,
   then test with a separate, ordinary account. Admin preview cannot impersonate a
   player or claim that public permissions have been validated by preview alone.
11. **Ready to invite:** summarize working links and any unfinished items. Completion
   does not publish the game, change registration policies or send invitations.

Admin tasks stay editable independently; optional tasks may be deferred. Existing
admins can start this guide without rerunning first-admin claim or database import.
Separate admin and player progress; a wizard trying the player tour does not
complete onboarding for everyone else. Player implementation and validation precede
admin polish, but both journeys are required for this project to be complete.

## State and data boundaries

Persist versioned progress per account and game: chosen path, completed step IDs,
skipped steps, dismissed/active state and image preference. Before login, use a
game-scoped browser record; after login offer to continue that progress once, rather
than silently overwriting an established account's choices on a shared browser.
No passwords, command text or message content are stored as onboarding progress.
Logout/account switching clears account-specific in-memory guide state.

Progress is not proof of competence. Opening a page or inserting a command does not
mark its lesson successful. Mark explicit practice completion or a scoped confirmed
client action, and permit “I already know this”. Retain progress across reload,
reconnection and route changes. Reevaluate character-dependent steps when characters
change, while keeping account-level learning. New guide versions do not reset opt-out.

Use the existing Mediator/store/cache architecture for durable writes, not a new
in-memory-only service. API authorization binds progress to the authenticated account;
the client cannot submit another account ID. Admin settings require the existing
configuration permission. Derive the exact persistence seam during implementation
planning, with explicit schema/version migration and tests.

Extend the existing `api/server-info` public presentation contract with a bounded
onboarding section for selected art IDs, public guide links and safe feature flags;
reuse its game name, logo, guest availability and `GameFeatures` rather than building
a competing source of game identity. `ServerInfoService` already refreshes stale
capabilities. A failed capability fetch must not assert that a lesson is available.
Do not expose or relax `api/mssp`, which is admin-only. Authenticated capability
responses respect the current character's access. Admin-only package inventory and
private wiki titles must never leak into anonymous guidance. Server responses are
authoritative; client-side tutorial choices grant no permissions.

The public `GameFeatures` list can gate broad scene/wiki/Discord lessons. Precise
package-command lessons need curated capability metadata tied to actual package
availability and access; do not interpret a broad feature flag as proof that every
example command will work. This metadata can be extended without tutorial code
querying administrator-only package APIs.

## Accessibility and tone

Plain language, localization keys, keyboard navigation, predictable focus return,
visible focus, reduced motion, and reflow at narrow widths/200% zoom are required.
No auto-advancing text, typing animation, autoplay audio or dependence on color.
Mascot illustrations are decorative when the adjacent text carries the meaning.
The journey and its commands remain usable with all images disabled.

Sharpie speaks briefly in first person where useful: “I'll help you find your feet.”
Avoid baby talk, congratulations for routine clicks, and fictional claims about what
the user has done. Display exact audiences before practice/live messaging.

## Evidence and validation

Repository baseline inspected: `ede117ea6` (2026-10-19). Relevant sources:
`Pages/Login.razor`, `CharacterCreate.razor`, `Play.razor`/`.razor.cs`,
`Components/Play/PlayComposer.razor`, `PlayGate.razor`, `Pages/Setup.razor`,
server `MsspController`/`SetupController`, and command attributes in
`SpeechCommands`, `GeneralCommands`, `HelpCommand`, `InteractionCommands` and
`SocketCommands`. Existing front-page design mentions an unimplemented welcome
modal; this proposal replaces that automatic modal with an opt-in guide.

Before claiming completion, demonstrate:

- Fresh Discord, MUSH, other-RP and skip paths on desktop and mobile, light/dark.
- Registration, returning login, configured external login, create, claim and
  character switching; recoverable failures keep the user's place.
- Browser reload, reconnect, logout, second account and guide-version changes.
- Real Command-mode `look`/`help` behavior and correct Say/OOC/Emit explanation;
  practice never produces a live message. No duplicated sends or lost drafts.
- Packages enabled/disabled/removed, unknown genre/subgenre, missing art, hidden
  controls and private wiki links. The guide neither leaks nor invents capabilities.
- Keyboard-only journey, screen-reader semantics and focus, narrow layout, zoom,
  reduced motion, no-art mode and zero forced onboarding when opted out.
- New-admin setup, interrupted resume, imported-world preservation, existing-admin
  entry, and ordinary-player verification after setup; unauthorized writes rejected.
- Appropriate service/component/integration tests, real browser screenshots and a
  requirement-to-evidence checklist. Automated checks do not substitute for manual
  readability, artwork and usability review.

Supporting docs must include player quickstart paths, a concise command reference,
what is new for MUSH users, admin configuration, and authoring/curating guide art.
Use the same verified command/capability catalog for UI and docs where practical.
Deliver implementation PRs and screenshots; do not merge or deploy without approval.

## Remaining artwork decisions

Opt-in entry, a compact contextual guide plus `/welcome`, no mascot chat, no
automatic execution of example commands, and player-first/admin-second scope are
approved. The user approved six styles and authorized the whole genre inventory in parallel
with implementation on 2026-10-19. Review new images as they arrive; do not hold
implementation on completion of the inventory.

### Bundled package provenance

Default packages installed from the reserved `bundled:sharpmush` source are
eligible for contextual command suggestions. They must still match a reviewed
version, the package-id source path, the reserved bundled commit marker, the
installed managed-attribute baseline and the current character's reachable
command handlers. They display “Included with this server” instead of a fabricated
Git reference. Git-sourced packages continue to require an approved repository,
a safe source path and a pinned commit URL. Unknown sources remain unadvertised.

### Artwork category identity

Artwork can declare `SharpieArtThemeKind.Genre` or `Subgenre`. The resolver checks
that kind as well as theme, style and topic, because MSSP includes `None` in both
category lists. Existing unambiguous entries without a kind remain compatible;
new entries should specify their kind. Explicit administrator artwork overrides
continue to select their named asset within the requested style/topic.

The two `None` inventories use distinct `sharpie-genre-none-...` and
`sharpie-subgenre-none-...` asset IDs and filenames. Their independent QA reports
also include the category kind. An approval in one inventory never approves the
other. Missing artwork continues through topic-aware subgenre/genre fallbacks to
the approved neutral mascot; no-image preferences still return no artwork.
