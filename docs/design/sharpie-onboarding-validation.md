# Sharpie onboarding validation

Status: implementation in progress; not accepted for merge or deployment.

## Current acceptance snapshot

The dated entries below retain the evidence as it was collected. Later entries
supersede earlier statements about missing publication or completed checks.

- Full component suite: **2,903 passed**, zero failed/skipped after the guide
  channel teaching and command-focus handoff (`guide-channels-tests.log`).
- Desktop dark/light journeys cover local practice, explicit terminal sending,
  guide dismissal/focus return, and verified installed-package help commands.
- Browser viewports at 390px and 320px showed no horizontal overflow; keyboard
  local practice and independent entry into Play were exercised. This is browser
  emulation, not physical-device or virtual-keyboard acceptance.
- Artwork preferences, no-art DOM absence, reload persistence, reduced mascot
  sizing and the welcome reduced-motion style were checked in the browser.
- Administrator settings preview/save, verified/deferred checklist persistence
  and ordinary-player denial were exercised against the isolated server.
- Whole-branch source review completed; its remaining entry-link localization
  finding is addressed and the fresh full component suite passes.
- Still open: broader keyboard/screen-reader checks, real failed-connection
  retry/claim journeys, published admin draft-feedback
  recheck, and PR/CI verification. The fresh genre-art inventory is not approved.

## Verified on 2026-10-19

- Full Blazor component suite: **2,873 passed, zero failed/skipped**.
- Targeted onboarding persistence/controller/catalog tests: **20 passed**.
- Disabled-wiki link regression: reproduced with a published page visible in
  onboarding while the reader was disabled; corrected and verified.
- Explicit accept/decline before importing anonymous progress, and preservation
  of future-schema local data, covered in the component suite.
- Isolated production-mode host with a fresh Lightning world and dedicated NATS:
  **12 real HTTP checks passed**. Disposable administrator/player accounts proved
  authorization, account isolation, optimistic-concurrency rejection and public
  identity/genre projection after an administrator save.

## Historical validation record

The sections below describe each check at the time it ran. The current acceptance
snapshot above is authoritative for remaining work.

The earlier browser failure is resolved. The local broker lost its port forward
after host thread exhaustion; the client also needed its separate publish output.
After restoring both, `/welcome` returned 200 and the in-app browser rendered the
Discord path and successful local `look` practice. Screenshot:
`work/onboarding/discord-practice-desktop.png`. This is one desktop dark-mode
interaction, not acceptance of all paths or keyboard/mobile behavior.

Additional checks: approved neutral mascot catalog 4/4 passed; all 113 direct
JavaScript tests across ten files passed. The client publish succeeded.

Independent review fixes now verified in component/catalog tests: four-step veteran
tour with separate completion, forum/tabletop explanations, authority-change
recovery, and an unsaved administrator identity preview. Five catalog tests pass.
Focus return, contextual/capability guidance and localization remain open, along
with the full runtime acceptance matrix.
Raw local logs and the credential-free HTTP status report are under
`work/onboarding/`; temporary test credentials are excluded from deliverables.

### Context and localization follow-up

All 2,880 component cases pass after localizing every player/admin lesson and moving contextual links into the lesson card. A selected-localizer regression and default-resource equivalence check cover all lesson paths. The isolated Production-hosted client was republished; browser inspection confirmed four MUSH steps, the distinct veteran lesson, and an enabled Scenes link inside that lesson. Screenshot: `work/onboarding/mush-context-refined.png` (dark desktop, preview mode). This is not proof of authenticated play, mobile layout, package command availability, or complete accessibility acceptance; those gates remain open.

### Package command discovery backend

The capability endpoint now supports reviewed plus-help1.8.0 and scene1.15.0 help commands, with installed version, pinned source reference, unchanged handler baseline and character-specific dispatch discovery. All27 onboarding server regressions pass, including unsupported versions, edited/shadowed/unreachable/mixed handlers, unsafe source paths and stale baselines. A separate real-engine regression passes: discovery returns a reachable handler without executing its body, then returns none after its use lock denies that character. Client presentation, installed-package end-to-end command results, and further adapters remain unverified.

### Signed-in runtime and connection recovery (2026-10-19)

- Restarted isolated loopback server with current capability backend (runtime session 46145). Signed in as the existing local test player and created JourneyTester without a native password. Creation succeeded but unavailable WebSocket gateway caused an unhandled CharacterCreate exception.
- Added retained-character connection retry with localized error and busy/finally guard. Handles false OTT result and network/cancellation failures without offering duplicate creation. Two regression cases reproduced failures before fix; CharacterCreate suite 6/6 and full component suite 2888/2888 pass (`work/onboarding/character-recovery-*.log`). Browser verification of repaired retry remains open; published client still predates this repair.
- Live signed-in guide correctly offered explicit anonymous-progress import/decline. Declined import, navigated to final lesson, and explicitly checked package suggestions for JourneyTester. Real backend returned the honest empty state; no positive installed-package claim. Screenshot `work/onboarding/signed-in-package-check.png`.
- Package UI uses captured session/character identity, cancellation and late-response guards, explicit GET only, and distinct empty/error states. Six package UI tests included in full suite.
- Remaining: publish repaired client and exercise recovery, run isolated socket/rendering services for positive connection/command journey, mobile/light/keyboard checks, remaining localization, installed-package walkthrough, logical commits/PR/CI.

### Sharpie visibility correction (2026-10-19)

User requested mascot presence throughout onboarding. GuidePanel now renders a compact approved mascot beside With Sharpie, so it remains present after the welcome header leaves view and in GuideCompanion during play. Welcome and companion pass the selected artwork and imagery preference. Unsupported/unapproved style inventory falls back to the approved neutral minimalist SVG; no-art still returns null, reduced uses 40px, standard 88px/64px responsive. No candidate art promoted. Added three component/catalog regressions; initial two failed as expected, full suite now 2891/2891 (`mascot-green.log`). Portal container breakpoint convention corrected to 48rem. Updated design and server catalog expectation. Published refreshed client successfully; real signed-in desktop screenshot `work/onboarding/sharpie-practice-visible.png` proves visible mascot in local practice card. Mobile and in-play visual acceptance remain open. Connection-recovery fix is now included in published client, but retry browser exercise remains open.

### Live terminal onboarding (2026-10-19)

Dedicated test gateway and renderer now run in containers. Gateway only publishes 127.0.0.1:4202; dedicated NATS publishes127.0.0.1:14223, server uses the existing isolated world on18080. Real browser OTT login connected JourneyTester. Found guide insertion only supported Story (unavailable without a scene). Added GlobalTerminal.TryInsertGuideCommandAsync: literal look only, interactive/connected/empty draft and identity rechecks; DOM check refuses composition/unreported draft. Reuses terminal focus, never sends. GuideCompanion's message no longer directs scene-less players to Story. Full bUnit2892/2892 pass; direct node layout35/35 pass (node --test only reported file wrappers, so direct result is the meaningful count). Required format passes complete.

Published and verified live: local practice stays separate; click inserts look into Game command with no new server output; explicit Send produces Room Zero output; Close guide removes companion and AX focus lands on Play container. Screenshot work/onboarding/sharpie-live-play.png shows mascot plus real room output. Mobile/light-mode/remaining command list and installed-package positive validation still open.

### Core-command walkthrough (2026-10-19)

Ran the remaining reference commands through the actual browser terminal on the isolated server: help say returned syntax and /noeval help; inventory returned empty inventory; who listed JourneyTester as the sole connected player; say echoed quoted speech; pose returned JourneyTester waves.; page to JourneyTester returned sender and recipient messages. AX evidence saved to work/onboarding/live-command-reference-evidence.txt. This single-character run verifies syntax and output, not multi-player audience isolation.

Added help say to GuideCatalog, distinct localized meaning, and rendered reference coverage. Corrected all novice/veteran/other-roleplay world and practice copy to acknowledge Story is available with a scene and ordinary Terminal supports first commands. Matching resource and docs updates pass full bUnit2892/2892 (verified-command-copy.log). The updated copy is built but not yet published to the running preview. Mobile/light testing remains unverified; CUA listed a CDP capability but no documented command interface was exposed in the current call, so no guessed API was used.

### UI localization completion pass (2026-10-19)

Moved Welcome familiarity choices, character routing text, skip state and account action; GuideCompanion controls/insert feedback; GuidePanel core-reference note; admin checklist labels/status/summary; GameIdentityForm fields, hints, preview, style labels and default SaveLabel; first-setup identity step into SharedResource. Existing English values preserved. GameIdentityForm accepts nullable SaveLabel to resolve the default through localization. Test fixture now registers localization as the real client does. Full bUnit2892/2892 pass (controls-localization.log and identity-localization.log), XML resource parse passes with zero duplicate keys, diffcheck clean. These changes are built but not published to preview yet. OnboardingService error strings remain to localize; do not claim full localization complete.

### Recovery review and light-mode acceptance (2026-10-19)

Localized the onboarding persistence errors. A failed guide reload now exposes a
collapsed recovery notice with Retry rather than hiding its only recovery control.
Character creation requires a successful initial roster read; a successfully
created character is retained when the subsequent roster read fails, and retry
verifies its DBRef and creation time without submitting creation again. An
additional character returns to the account or guide without changing the active
character. Independent follow-up review confirmed both fixes. Full bUnit2895/2895
passed; two additional routing cases brought the targeted CharacterCreate suite
to10/10. Required two-pass formatting and diff checks passed after the additional tests.

Published the tested recovery client (`publish-recovery.log`). Real browser checks:
other-roleplay selection and No artwork persisted after refresh; keyboard selected
No artwork and the page contained zero images; Reduced rendered a96px header mascot
and40px lesson mascot, both loaded. Evidence: `reduced-art-keyboard.png`. Existing
local test account login honored `/settings/theme` return routing. Selected Daylight
for the disposable JourneyTester character and verified the veteran welcome and
lesson in light mode: `veteran-light-desktop.png`, `veteran-light-lesson.png`. The
four-step veteran tour keeps its distinct introduction and optional command
reference. This does not establish mobile or comprehensive accessibility acceptance.

### Mobile and independent exploration (2026-10-19)

Used the supported browser viewport capability at390x844 and320x740. At390px,
expanded command help wrapped without horizontal overflow (document scrollWidth
and clientWidth both390). At320px, Discord local practice accepted `look` via
keyboard Tab/Enter, displayed the courtyard result and kept scrollWidth equal
to clientWidth320. Screenshots: `mobile-command-reference.png`,
`mobile-practice-320.png`. With reduced motion emulated, the welcome container
had animation-name none and transition-duration0s; this is a scoped style check,
not comprehensive screen-reader proof. The practice result has role=status.

Choosing independent exploration at320px navigated directly to Play with no
companion. OTT connected JourneyTester, and explicitly sending `look` returned
Room Zero output from the isolated server. Screenshot `mobile-independent-play.png`.
Reset both viewport and emulated media afterward. These are real browser journeys
at mobile viewport sizes, not physical-device or virtual-keyboard validation.

The positive package check exposed a bug: the server already installs plus-help
1.8.0 and scene1.15.0 from bundled:sharpmush, but the capability resolver only
recognizes pinned Git sources. A regression is being added; this gate remains open.

### Bundled package suggestions fixed and verified (2026-10-19)

The new bundled-source test reproduced the omission (12/13 passed, bundled
case failed). The resolver now recognizes only the reserved source and commit
marker with matching package-id path, while keeping reviewed-version, live
reachability and managed-baseline checks. Git sources retain their pinned URL
requirements. Bundled suggestions have a nullable reference and a localized
“Included with this server” label. Independent review found no concrete issue.
All13 server capability cases and the full2898 component cases pass. Published
client and restarted only the isolated loopback server.

Real browser evidence: explicit checks for JourneyTester return +help1.8.0 and
+scene/help1.15.0. Both commands were separately sent through the real terminal:
+help returned the installed topic sources; +scene/help returned scene command
syntax. Evidence: `package-help-live.txt`, `installed-package-suggestions.png`.
Screenshot review found source-label spacing, corrected with an explicit separator;
republished successfully and verified the explicit separators in the live accessibility tree. The full-page screenshot includes Sharpie and both suggestions.

### Administrator journey (2026-10-19)

The ordinary player was denied `/admin/onboarding`. Signed in with the disposable
administrator and reviewed prefilled settings. Changed introduction and selected
Comicbook style; the unsaved preview updated. Saving persisted the settings and
marked identity verified. Deferred joining, reloaded, and confirmed one verified
and one deferred item plus the saved introduction/style. The intentionally unsaved
later draft was discarded on refresh. Screenshot: `admin-checklist-persisted.png`.

Found and fixed stale Saved feedback after editing: targeted regression failed
before the fix and passed afterward; every bound identity field now clears that
feedback. Full component suite still passes 2,898 cases. The joining checklist link
now targets the actual player access controls (`/admin/config/Net`), verified in the
browser. Default wiki templates no longer advertise obsolete `create` login syntax;
they link to the optional guide and current account/character flow. Existing game
wiki content is not migrated or overwritten.

### Draft PR and further keyboard checks (2026-10-19)

Draft PR: https://github.com/SharpMUSH/SharpMUSH/pull/2069.
The published implementation head is 1d52c080a. Formatting CI passed; the build
was still running when checked. The automated Claude review failed immediately
after initialization with no findings and was retried once. This is not a review
pass; CodeRabbit explicitly skipped the draft.

In the actual browser, an expired disposable account session rejected a guide
choice with a recoverable save error instead of claiming success. Reload returned
to anonymous browser progress. Keyboard activation advanced the other-roleplay
path past its already-completed steps to the audience/consent lesson; Return
expanded the command reference. The accessibility tree exposed the new heading,
expanded state and command/audience definitions. The reduced Sharpie remained
visible. Screenshot: `work/onboarding/roleplay-keyboard-reference.png`.
This verifies these interactions, not a complete screen-reader assessment.

### Additional-character runtime journey (2026-10-19)

Signed in through the visible login form as the disposable player with Remember me
unchecked. Created JourneySecond using the existing no-chosen-password option.
The portal returned to /account, listing JourneyTester (#17) and JourneySecond
(#18). Opening Play authenticated JourneyTester through OTT and displayed Room
Zero; creating the additional character did not switch the active character.
The contextual Sharpie guide remained available and expanded successfully.
Screenshots: `additional-character-account.png` and
`additional-character-original-play.png` under `work/onboarding/`.
The claim and failed-connection retry journeys remain open.

### Play-height correction (2026-10-19)

User review rejected the guide stacked above Play. Replaced it with an explicit
header control opening the existing dismissible side-sheet surface. The terminal
remains mounted and no guide content occupies the Play flex column. Desktop
measured 918x836 before and during opening; at a 390x844 viewport Play remained
374x828, with document scroll width 390. Escape closed the sheet and restored
focus to the opener. Screenshot: `work/onboarding/play-guide-closed.png`.
A component regression checks that the dialog is outside the Play layout and
the game card remains mounted. This panel is modal while explicitly open; close
it to resume typing. Further refinement of guide insertion/focus is pending.


## Guide navigation and review follow-up — 2026-10-19

The Play guide now has one sheet boundary, with both inner card borders removed.
Back to game and Open the full guide or change my starting point are button-styled
controls together above the lesson. The latter remains a semantic navigation link.
There is one full-guide link in the sheet for all progress states. The outer
accordion is gone; the optional command reference retains its disclosure.
Back to game preserves the journey, while Stop this guide dismisses it.

Published client checked in the real browser: both top controls are present,
Sharpie remains visible, and the inner frames are absent. Evidence:
`work/onboarding/guide-navigation.png`. Viewport screenshot capture was used;
full-page capture resizes this responsive surface and closes the sheet.
Full bUnit suite: 2,901 passed, zero failed/skipped. Changed C# whitespace and
`git diff --check` pass. Successful insertion closing the panel is component-tested;
its latest real-browser acceptance remains open.

Ten automated review threads were answered with ownership/control-flow evidence:
six response-disposal warnings transfer ownership to consumers that dispose the
response; three state guards intentionally recheck after awaited JS calls; the
optional presentation boundary logs failures, propagates caller cancellation,
and returns no unverified links. No safety guards were removed to silence these
findings. CI and broader end-to-end acceptance remain required.


## Icon controls and live draft protection — 2026-10-19

Published client at `058830c83`: Back to game and full-guide navigation are
icon-only controls with localized accessible names/tooltips. Stop this guide is
also an icon, and the lesson header no longer displays “With Sharpie”. An inactive
journey now offers four starting points inside the Play sheet rather than leaving
it empty. Browser selection starts/resumes the lesson in place. The full component
suite passes 2,902 tests, including the new inactive-path regression.

Live account/terminal evidence on the isolated world: inserting `look` into an
empty connected input closed the sheet without sending and retained the current
lesson on reopening. A second insertion with that draft already present left
`look` unchanged, kept the sheet open, and explained the refusal. Back to game
preserved the draft; only explicit Send echoed `look`, cleared the input, and
produced the room response at 23:17:34. Screenshots:
`work/onboarding/guide-stop-icon.png` and
`work/onboarding/guide-draft-preserved.png`.

Focus currently returns to the guide opener after successful insertion; improving
that handoff to the command input remains an accessibility/usability follow-up.
Character claim, failed-connection retry, administrator draft-feedback runtime
checks and broader accessibility acceptance remain open. Genre lesson pilots
are under independent visual review and are not shipped or owner-approved.


## Channels and command focus — 2026-10-19

Published client includes `a3e7e1305` channel guidance and `422af8a37` focus fix.
The final lesson in every path teaches Browse channels, `@channel/list`, reading
channel descriptions, joining, chatting and leaving, with explicit cross-room
audience and game-dependent names/permissions. Reference commands are localized.
The live ordinary-player terminal accepted `@channel/list` and returned the
correct empty-world result, “No channels.” Join/chat syntax was checked against
shipped channel help; no public message or membership change was made.

Real-browser insertion into the empty connected input closed the guide and left
focus on Game command with value `look`, without sending. This supersedes the
previous focus follow-up. Standard Back to game still returns to its opener.

The channel lesson's document scroll width equaled the viewport at 390px and
320px. At 390px its content width was 314px; command examples wrapped within the
card. Enter expanded and collapsed the common-command reference with focus on
its summary; channel entries displayed their localized meanings and audiences.
The viewport override was reset afterward. These are browser viewport/keyboard
checks, not physical-device or screen-reader acceptance. Screenshot:
`work/onboarding/channels-phone.png`.
