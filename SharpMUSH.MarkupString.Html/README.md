# MarkupString.Html

Raw HTML tag markup for [`MarkupString`](https://www.nuget.org/packages/MarkupString): an MXP
`<send>`, an anchor, a `<div class="...">` — carried as a layer over a span of text and written as
itself in the `Html`, `Pueblo` and `Mxp` formats.

Where a tag has no meaning — a terminal — it does not vanish silently: `b`, `strong`, `i`, `em`,
`u`, `s`, `strike` and `del` fold into the run's terminal styling through
[`MarkupString.Ansi`](https://www.nuget.org/packages/MarkupString.Ansi), and any other tag leaves
its body untouched.

```sh
dotnet add package MarkupString.Html
```

## Usage

```csharp
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

var text = MarkupText.Wrap(
  HtmlMarkup.Create("send", "href=\"n\""),
  MarkupText.Wrap(AnsiCodeParser.Parse("hr"), "north"));

text.Render(MarkupFormat.Html);   // <send href="n"><span style="color: #ff5555">north</span></send>
text.Render(MarkupFormat.Ansi);   // \e[1;31mnorth\e[0m
text.Render(MarkupFormat.Plain);  // north

var json = MarkupTextSerializer.Serialize(text);
var back = MarkupTextSerializer.Deserialize(json);
```

`WithAnsi()` must be applied as well: the terminal fold for `b`/`i`/`u`/`s` comes from that
package. Neither the tag name nor the attribute string is sanitised — validate what you put in one.

## Styling

The HTML emitters write `ms-*` classes for the text attributes that have a fixed rendering, and
inline `style` for colours, which are open-ended. `HtmlCss.Fixed` is the stylesheet for those
classes — include it once per page, or copy its rules into your own sheet.

## Extension points

- `IMarkup` + `IMarkupEmitter` — register an emitter for `HtmlMarkup` in a format of your own, or
  replace this package's by adding yours to the registry afterwards.
- `IMarkupCodec` — `HtmlMarkupCodec` is the wire shape under kind `"html"`.
- `IAnsiStyleSource` — `HtmlMarkup` implements it; that is how a tag becomes terminal styling.

## AOT and trimming

`IsAotCompatible`; no reflection, no dynamic code. Registration is an explicit `WithHtml()` call.

## Licence

Apache-2.0. Part of [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
