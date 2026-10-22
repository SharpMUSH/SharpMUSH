<!-- help-article
{
  "corpus": "help",
  "id": "figure-function",
  "lookup": "figure()",
  "aliases": [],
  "sections": [
    {
      "id": "arguments",
      "heading": "Arguments",
      "lookup": "figure() arguments"
    },
    {
      "id": "who-sees",
      "heading": "Who sees the picture",
      "lookup": "figure() who sees the picture"
    },
    {
      "id": "example",
      "heading": "Example",
      "lookup": "figure() example"
    }
  ]
}
-->
# figure()

`figure(<address>[, <description>[, <art>[, <float>[, <beside>[, <width>]]]]])`

A picture, the text art a terminal shows instead of it, and the text that goes beside it.

- [figure() arguments]: the address, description, art, float and text beside
- [figure() who sees the picture]: who is shown the picture, and who the art
- [figure() example]: a cat with text beside it

::: seealso
- [IMAGE()]
- [MEDIA FUNCTIONS]
- [LAYOUT FUNCTIONS]
:::

## Arguments

- *<address>* - the picture, as for [IMAGE()].
- *<description>* - what it shows, for a reader who cannot see it.
- *<art>* - the text art a telnet client sees; its lines keep their own spacing.
- *<float>* - `none` (the default: the picture on its own lines, *<beside>* under it), `left` or `right`.
- *<beside>* - the text beside the picture. It can be a layout, such as [FIELDS()].
- *<width>* - the width of the whole figure; see [LAYOUT FUNCTIONS] for leaving it out.

Floated, *<beside>* flows down the other side of the art in a telnet client and wraps back to the full width once past it; in the web portal, it flows round the picture.

## Who sees the picture

The picture is shown only when the caller may use image() (a Wizard, or the Send_Image or Send_OOB @power; the approved role holds Send_Image) and the `image_hosts` option allows its host. Otherwise the portal shows the art too, or the description when there is none.

## Example

```sharp
> think figure(https://example.com/cat.png,A cat,/|_/|%r=^.^=%r%b> <,left,The cat sits by the fire and watches the door all night long.,24)
/|_/|  The cat sits by
=^.^=  the fire and
 > <   watches the door
all night long.
```
