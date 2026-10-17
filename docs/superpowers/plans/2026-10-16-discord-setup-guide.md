# Illustrated Discord setup guide implementation plan

User authorization: implement issue #1997, including genuine local UI captures. External Developer Portal screenshots failed through the supported browser route; use actual SharpMUSH components with synthetic API fixtures and label them as representative examples. Never reveal or generate credentials, mutate a bot, or send external messages. Provider-specific instructions link to current official Discord documentation.

- [x] Add a regression that the registered setup section and alias retain a stable guide link in portal and terminal rendering.
- [x] Render real localized Admin > Discord and Secrets components against synthetic read-only fixtures; assert connection checklist/access controls and empty secret field; capture optional HTML for genuine screenshots.
- [x] Write sequential guide: application/bot, required intents, invite permissions, token storage/reconnect, success checks, Developer Mode/IDs, channel access, member linking and per-channel speaking. Explain OAuth sign-in is separate.
- [x] Capture desktop and narrow examples using current generated CSS, verify no overflow, display assets with Codex-show, and include descriptive alt text/captions.
- [ ] Link guide from focused setup help without bloating terminal help. Run focused docs/component checks, obtain parent review and create separate ready PR. Explicitly document any unmet issue acceptance; do not claim unseen provider screenshots exist.

Builds use `flock --close /temp/codex/sharpmush-build.lock dotnet build ... -m:2 -nr:false`; focused TUnit tests use `--maximum-parallel-tests 8`. Preserve existing untracked work/.

Verification: setup renderer RED for missing link, then RED for a titled link hiding the URL in plain output; readable URL GREEN, including alias, portal href and 31-row bound. New component tests 2/2 GREEN with real theme/localizer. Current CSS geometry is clean at 390/1100 for both pages. Three JPEGs inspected, alt text and local targets verified. `Codex-show` was unavailable on the host; no alternate desktop opener was used. Provider-specific screenshots remain represented by official documentation links; the guide explicitly labels its genuine local example captures. Existing AdminDiscordPageTests 9/9 and SecretsPageTests 7/7 also passed. Parent review and PR are still pending.
