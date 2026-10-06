<!-- help-article
{
  "corpus": "help",
  "id": "ansi-function",
  "lookup": "ansi()",
  "aliases": [],
  "sections": [
    {
      "id": "legacy-color-codes",
      "heading": "Legacy color codes",
      "lookup": "ansi legacy color codes"
    },
    {
      "id": "color-examples",
      "heading": "Color examples",
      "lookup": "ansi color examples"
    }
  ],
  "redirects": {
    "ANSI2": "ansi legacy color codes",
    "ANSI3": "ansi color examples"
  }
}
-->
# ansi()

`ansi(<codes>[ ... <codesN>], <string>)`

This allows you to mark up a string using ANSI terminal effects, 16-color codes, and 256 XTERM colors (specified as color names or hex values).

The old-style `<ansi-codes>` are listed in "help ansi legacy color codes".<br>
Each block of space-separated `<codes>` can be one or more old-style ANSI codes, as listed in "help ansi legacy color codes", or a foreground and/or background color. Background colors are prefixed with a "/". Each color can be one of:

    * +`<colorname>` (for a list of valid names, see [colors()])
    * a hexcode, optionally in angle brackets (#000000, `<#ff0055>`, etc)
    * a list of red, green and blue values from 0-255, in angle brackets (`<0 0 0>`, `<255 0 85>`, etc)
    * a number from 0-255; this is the same as using "+xterm`<number>`", for Rhost compatability.

For example, "ansi(+orange/#0000ff,Test)" would color "Test" in orange, on a blue background. In the event that your client does not support those colors, SharpMUSH will downgrade the color to the closest fit that your client can understand.

Codes are parsed from left to right so, with later codes overriding earlier ones. So, for example:
```sharp
ansi(y /+green B <#ffffff>, test)
would show white text on an ANSI-blue background.
```

::: seealso
- [ANSI]
- [COLOR]
- [@SOCKSET]
- [COLORSTYLE]
- [colors()]
:::

## Legacy color codes

Old-style valid color codes are:
```text
        f - flash                       F - not flash
        h - hilite                      H - not hilite
        u - underscore                  U - not underscore
        i - inverse                     I - not inverse
        n - normal

        d - default foreground          D - default background
        x - black foreground            X - black background
        r - red foreground              R - red background
        g - green foreground            G - green background
        y - yellow foreground           Y - yellow background
        b - blue foreground             B - blue background
        m - magenta foreground          M - magenta background
        c - cyan foreground             C - cyan background
        w - white foreground            W - white background
```
For example, "ansi(fc, Test)" would hilight "Test" in flashing cyan. Default foreground and background use the client's default color for fore and back.

## Color examples

Bright yellow text on a blue background:<br>
```sharp
> think ansi(yB, foo)
```

Orange text on an ANSI-green background:<br>
```sharp
> think ansi(G+orange, bar)
```

Underlined pink text on a purple background<br>
```sharp
> think ansi(u+lightsalmon/#a020f0, ugly)
```

ANSI-blue text on a bisque background<br>
```sharp
> think ansi(+yellow/+bisque b, the 'b' overrides the earlier '+yellow')
```
