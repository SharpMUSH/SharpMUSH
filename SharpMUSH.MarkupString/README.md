# MarkupString

Immutable styled text for terminals and the web: a plain string plus layered markup runs over it.
One value renders to ANSI, HTML, Pueblo, MXP, BBCode or plain text, and round-trips through JSON
without losing a layer it does not understand.

Slicing, padding, wrapping and the rest of the string operations work in display cells (wide CJK,
combining marks and emoji sequences count correctly), and they carry the markup with them.

```sh
dotnet add package MarkupString
```

Rendering needs emitters, which live in the format packages — add
[`MarkupString.Ansi`](https://www.nuget.org/packages/MarkupString.Ansi) and
[`MarkupString.Html`](https://www.nuget.org/packages/MarkupString.Html) for the built-in six.

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
text.Render(MarkupFormat.Plain);  // Hello, world

var json = MarkupTextSerializer.Serialize(text);
var back = MarkupTextSerializer.Deserialize(json);
```

## Formats

`Plain`, `Ansi`, `Html`, `Pueblo`, `Mxp`, `BBCode`. `MarkupFormat.Custom(name, encoding)` declares
one of your own; a registry keyed by it renders through your emitters like any built-in.

## Extension points

- `IMarkup` — a markup layer. Value equality makes identically marked spans coalesce.
- `IMarkupEmitter` / `IMarkupSetEmitter` — how a layer, or a whole run's stack of layers, is
  written in one format.
- `IFormatFramer` — a prologue and epilogue around a rendered document.
- `IMarkupCodec` — the JSON shape of a layer. A layer with no codec at read time survives as
  `UnknownMarkup` and is written back verbatim, so an old reader does not destroy new markup.

Build a `MarkupRegistry` from `MarkupRegistry.Empty` with `With(...)`, pass it per call, or install
it once as `MarkupRegistry.Default`.

## AOT and trimming

`IsAotCompatible`; no reflection, no dynamic code, no source generators. Registration is explicit,
so nothing needs a trimmer root beyond what you reference.

## Licence

Apache-2.0. Part of [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
