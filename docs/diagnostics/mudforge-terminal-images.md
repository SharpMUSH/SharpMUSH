# MudForge terminal images

## Changes and release order

1. MarkupString 2.27.1 contains the companion change: an unsupported MXP IMAGE delegates to the
   configured ANSI picture emitter. Supported MXP elements remain MXP; disabled graphics or
   missing pixels retain the text art.
2. `MarkupStringVersion` in `Directory.Build.props` is pinned to 2.27.1. Restore and test the
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

These changes were tested locally, not deployed to the logged-in game. After release and deployment,
verify both `SOCKSET graphics=iterm2` and `SOCKSET graphics=sixel` followed by `look` in room zero.
Confirm actual OSC 1337/DCS Sixel output and visible images while MXP command links continue working.
Automatic detection still requires MudForge to forward terminal replies.

## Validation

- MarkupString: all 1,591 tests passed, including MXP fallback, supported IMAGE preference, missing
  pixels, disabled graphics, secure framing, command links, and block-art background restoration.
- SharpMUSH: 88 rendering tests and 79 output-renderer tests passed against the final local packages.
  WebP tests cover exact RGBA bytes including alpha, malformed input, direct local fetching, and
  WebP-to-Sixel/iTerm2 output with MXP active and IMAGE unsupported.
