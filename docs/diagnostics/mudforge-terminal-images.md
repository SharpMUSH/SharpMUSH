# MudForge terminal images

## Changes and release order

1. MarkupString 2.27.1 contains the companion change: an unsupported MXP IMAGE delegates to the
   configured ANSI picture emitter. Supported MXP elements remain MXP; disabled graphics or
   missing pixels retain the text art.
2. `MarkupStringVersion` in `Directory.Build.props` is pinned to 2.27.2. Restore and test the
   published packages once NuGet propagation finishes before shipping this renderer change.
   Initial local validation used all five packages built together as `2.25.1-mudforge2`, via an
   MSBuild property override; that version was a local test artifact, not a published dependency.
3. No TNC prerequisite: terminal replies without CR/LF were fixed in TNC 4.5.2; current SharpMUSH
   already consumes 4.6.1. Do not use ECHO negotiation as a graphics-capability signal.

SharpMUSH owns HTTP fetching and image decoding. Its renderer now decodes WebP to straight RGBA
using SkiaSharp before handing pixels to MarkupString, retaining download, dimension and stored-size
limits. Animated WebP currently supplies a still frame; existing GIF animation is unchanged.

MarkupString's incoming iTerm2 decoder already recognizes WebP and retains encoded image bytes
for browser display. That is separate from the server's need for decoded pixels to produce Sixel.

## Runtime evidence

MudForge's web client had its xterm image addon enabled for Sixel and iTerm2, but its terminal had
`disableStdin=true` and no data listeners. The graphics probe reached the browser; replies were not
forwarded. Forcing a graphics method with SOCKSET bypasses detection, but previously MXP rendering
still selected text fallback because IMAGE was unsupported. A direct PNG test as a wizard also
produced only its description. WebP then presented an independent decode failure in the renderer.

On 2026-11-07, production server and renderer revision
`9b63287054c4eeb93e065fe78ab04c8cf77ac1a7` (containing PR #2589) were verified running.
MudForge 2.0.5657 displayed room zero's WebP through both forced iTerm2 and forced Sixel.
Fresh WebSocket traffic contained OSC 1337 or DCS Sixel respectively, with no unsupported MXP IMAGE.

### iTerm2 placement at high DPI

A subsequent repeated-`look` test reproduced pictures above their reserved room panels and over
unrelated text at `devicePixelRatio=2`. MudForge's loaded bundle
`/_next/static/chunks/4728.402d71c64046c398.js` applies a sizing correction to **every** terminal
canvas: CSS width/height become `Math.round(canvas.width / devicePixelRatio)` and the corresponding
height, using inline `!important` styles.

The text canvas had backing dimensions 1744×1462 and CSS dimensions 872×731, as expected. The
`.xterm-image-layer` already had backing dimensions 872×731, but the correction forced its CSS
size to 436×366. This scales both image sizes and their positions by one half. Temporarily restoring
that layer's CSS size to 872×731 put two successive iTerm2 pictures inside their own room panels.
The experiment was reverted; the Wizard session remained open with its initial `graphics=iterm2`.

MudForge should exclude `.xterm-image-layer` from its generic device-pixel-ratio correction and
let the image addon own the layer's dimensions. Verify this across DPR 1/2, font changes, resizing,
and scrolling. Do not compensate by changing SharpMUSH's cursor coordinates or image size.
Images can also appear after the surrounding text while the browser asynchronously decodes them;
wait for decoding to settle before judging a blank panel.

### Remaining capability and quality work

SharpMUSH preserves unanswered Sixel probes as unknown, separately from explicit negative device
attributes, just as it does for Kitty. A missing reply must not erase a known terminal profile.
The companion MarkupString refinement recognizes MUDFORGE's verified still iTerm2/Sixel support
and uses an adaptive Sixel palette. Those refinements ship in MarkupString 2.27.2, now pinned by this branch.
Production gains them after this SharpMUSH update is merged, published and deployed.
Graphics remains opt-in (`SOCKSET graphics=auto` or a specific method); naming a terminal does not
enable pictures by itself. No TNC change is needed.

MudForge should still forward xterm terminal replies, including the cell-size response. Sixel needs
pixel dimensions to match cell geometry; guessed font dimensions cannot remain correct across
font/zoom changes. The named profile does not repair that missing feedback channel.

## Validation

- MarkupString: all 1,591 tests passed, including MXP fallback, supported IMAGE preference, missing
  pixels, disabled graphics, secure framing, command links, and block-art background restoration.
- SharpMUSH: 88 rendering tests and 79 output-renderer tests passed against published MarkupString 2.27.1.
  WebP tests cover exact RGBA bytes including alpha, malformed input, direct local fetching, and
  WebP-to-Sixel/iTerm2 output with MXP active and IMAGE unsupported.

## Automatic probing and evidence

`SOCKSET graphics=auto` (including `on` and `yes`) requests the existing TNC terminal probes only for a
known terminal: its first terminal type names a profile, a `SOCKSET terminal` pin names one, or an earlier
XTVERSION answer did. A MUD client or unknown client would print the queries, so it is not asked.
`detect` asks any client. The same option handler serves `@SOCKSET`.
The command does not wait for replies and ordinary input is not blocked. A terminal can answer
without Enter; selecting `auto` again reissues the queries to a known terminal. Explicit methods and `off` do not probe.

`SOCKSET` separates the selected output method from each protocol's evidence. Positive or negative
probe results take precedence over profile assumptions. Profiles are labelled `unconfirmed`, and
missing evidence is `unknown`; neither a manual override nor a terminal name is reported as proof.
A previously received answer remains the last observed evidence until another answer replaces it.
Cell dimensions are reported separately. A client with an unrecognized name can select Sixel or
Kitty through a protocol reply; iTerm2 image capability has no equivalent probe here.

A future explicit capability advertisement should use an agreed, versioned client/server contract,
including protocol support, animation, payload limits and cell dimensions. GMCP is a possible
transport, not an implemented standard in this change. Keep that work separate from client identity;
validate bounds and handle capability changes before adding renderer selection based on it.

Validation of automatic probing against all five published MarkupString 2.27.2 packages:
27 capability/option tests, 41 socket-command tests, 88 rendering tests and 79 output-renderer tests
passed. The radio-table fixture was regenerated with the package's renderer; its HTML is unchanged.
