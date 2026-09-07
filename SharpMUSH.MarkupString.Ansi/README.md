# MarkupString.Ansi

Terminal styling for [`MarkupString`](https://www.nuget.org/packages/MarkupString): colours (the 16
standard ones, xterm-256 and truecolor), bold/faint/italic/underline/overline/strike/blink/invert,
and links — either a URL or a command for the client to send.

One `AnsiMarkup` layer per span; a run's stack of layers folds into a single SGR sequence for the
terminal, one `<span>` for HTML, or the nearest equivalent in the other formats. Transitions are
written as diffs, so nested spans do not restate what is already in effect.

```sh
dotnet add package MarkupString.Ansi
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

var text = MarkupText.Concat(
  MarkupText.Plain("Hello, "),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "world"));

text.Render(MarkupFormat.Ansi);   // Hello, \e[1;31mworld\e[0m
text.Render(MarkupFormat.Html);   // Hello, <span style="color: #ff5555">world</span>

var json = MarkupTextSerializer.Serialize(text);
var back = MarkupTextSerializer.Deserialize(json);
```

`AnsiCodeParser.Parse` takes MUSH `ansi()` syntax — `hr`, `/R`, `+xterm200`, `#ff5555`,
`<255 0 0>` — and `AnsiMarkup.Create(...)` builds the same thing from named arguments.

## Formats

`WithAnsi()` registers emitters for `Ansi`, `Html`, `Pueblo`, `Mxp` and `BBCode`. `Plain` needs
none: the body passes through.

## Extension points

- `IAnsiStyleSource` — implement it on your own `IMarkup` and this package's fold picks the style
  up, so a layer of yours can contribute bold or a colour without its own emitter per format.
- `IMarkupEmitter` / `IMarkupSetEmitter` / `IMarkupCodec` — replace any of this package's
  registrations by adding yours to the registry afterwards.

## AOT and trimming

`IsAotCompatible`; no reflection, no dynamic code. Registration is an explicit `WithAnsi()` call,
so nothing depends on a type surviving the trimmer by name.

## Licence

Apache-2.0. Part of [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
