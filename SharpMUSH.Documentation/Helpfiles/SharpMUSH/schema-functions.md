<!-- help-article
{
  "corpus": "help",
  "id": "schema-functions",
  "lookup": "schemarender()",
  "aliases": ["schemarender", "schemacheck()", "schemacheck", "schema functions"],
  "sections": [
    {
      "id": "options",
      "heading": "Options",
      "lookup": "schemarender() options"
    },
    {
      "id": "check",
      "heading": "Checking a document",
      "lookup": "schemacheck() problems"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "schemarender() examples"
    }
  ]
}
-->
# schemarender()

`schemarender(<schema>, <data>[, <options>])`
`schemacheck(<schema>[, <data>])`

A portal view or form drawn in the terminal. `<schema>` is the Portal Schema Document an application serves, and `<data>` is what its data route answers: `{"fields":{"<key>":{"value":...,"visible":...}}}`. The portal draws the same document from the same data, so a sheet is written once.

A field or value the data hides (`"visible":false`) or leaves out is not drawn. Stat tiles show a bar, or pips when the tile sets `pips`. A picture shows its address. A table becomes a listing.

schemacheck() returns nothing when the document is sound, or one problem per line.

- [schemarender() options]: the page and the width
- [schemacheck() problems]: what it reports
- [schemarender() examples]: a finger sheet and a broken one

::: seealso
- [LAYOUT FUNCTIONS]
- [FIELDS()]
- [DATATABLE()]
:::

## Options

The options are a JSON object, and may be left out:
- `"page":"<key>"` - the page of a tabbed view to draw. The first page is drawn when none is named, and the others are listed under it. A view that is not tabbed draws every page.
- `"width":<n>` - see [LAYOUT FUNCTIONS].

## Checking a document

Each problem is `<path>: <problem>`, in document order. schemacheck() reports:
- an unknown document kind, element kind or field type
- a field with no key, or a key used twice
- a select, radio or multiselect with no options
- a table with no columns, or an element with no field to read
- a button with no action, or an action not among the document's `actions`
- an action route that is not on this server
- a page's `next` or `prev` naming no page
- a span wider than its section's columns

Given `<data>`, it also names each field an element reads that the data does not hold. A key it does not know is never a problem.

## Examples

```sharp
> &SHEET me={"kind":"view","title":"Finger","pages":[{"sections":[{"elements":[{"kind":"keyvalue","fields":["Name","Plays","Tags"]}]}]}]}
> &FACTS me={"fields":{"Name":{"value":"Raya"},"Plays":{"value":"Thursdays"},"Tags":{"value":["scenes","plots"]}}}
> think schemarender(v(SHEET),v(FACTS),{{"width":40}})
+==============< Finger >==============+
| Name:  Raya                          |
| Plays: Thursdays                     |
| Tags:  scenes, plots                 |
+======================================+
```

```sharp
> &SHEET2 me={"kind":"view","pages":[{"sections":[{"elements":[{"kind":"table","rows_field":"Gear"},{"kind":"dial"}]}]}]}
> think schemacheck(v(SHEET2),v(FACTS))
pages[0].sections[0].elements[0]: rows_field "Gear" is not in the data
pages[0].sections[0].elements[0]: a table needs columns
pages[0].sections[0].elements[1]: unknown kind "dial"
```
