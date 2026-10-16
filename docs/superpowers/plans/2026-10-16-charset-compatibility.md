# Charset compatibility implementation plan

**Goal:** Resolve #1798 with exactly UTF-8, ISO-8859-1 and US-ASCII offers, consistent text decoding/rendering, and fixed-UTF-8 GMCP.

**Approved scope:** Charset lane of the 2026-10-16 open issue consolidation. Independent branch from main `38bab40c33ce362fe147b9b1d209367a02193b55`; no merge or deployment. Prefer supported TNC configuration; never expand the renderer to arbitrary .NET encodings. Reject or terminate unsupported/unoffered ACCEPTED, including buffered input after the frame.

**Architecture:** Configure CharsetProtocol.AllowedEncodings with the renderer's three code pages and explicit preference. Exercise the real interpreter and connection handler through the existing duplex-pipe harness. TNC 4.5.0 commits ACCEPTED before its callback, has no public outstanding-offer hook, and decodes GMCP using the text encoding; investigate a dependency fix before claiming complete enforcement. No reflection or vendored dependency sources.

**Files:** TelnetServer.Session.cs owns plugin configuration; TelnetServerCharsetTests.cs extends the existing negotiation fixture with byte-level tests; existing TerminalFeatureRenderingTests covers renderer policies. The separate dependency branch owns negotiation-state and GMCP protocol fixes.

- [x] Add and run failing exact-wire offer, text/renderer agreement, unsupported/unoffered acceptance and fixed-UTF8 GMCP regressions.
- [x] Configure public AllowedEncodings and preserve UTF-8, Latin-1, ASCII preference.
- [x] Implement dependency enforcement or a safe connection boundary, retaining fragmented/coalesced frame coverage, aliases, and rejection behavior.
- [x] Run negotiation and affected renderer test classes individually, format twice, record dependency revision and limits.
- [x] Commit/push reviewable changes; coordinator owns review and PR.

## Dependency investigation

NuGet 4.5.0 nuspec pins `0ad3558f2b69d1455948ac0c1e8359b3c734c700`. Its CharsetProtocol.CompleteAcceptedCharsetFromBytesAsync accepts every Encoding.GetEncoding name, commits both encodings before callback, and swallows callback exceptions. CharsetRejectedAsync is a no-op without a consumer event. GMCPProtocol decodes data with context.CurrentEncoding and SendGMCPCommand(string,string) encodes it likewise. A callback-only allowlist cannot enforce outstanding offers or preserve fixed-UTF8 GMCP.

## Delivery status

Implementation and local-package verification are complete; production release integration remains blocked on a reviewed TNC package. See `docs/testing/charset-compatibility.md` for exact source/version, commands, acceptance evidence and the required post-release checks. No merge or publication was performed.
