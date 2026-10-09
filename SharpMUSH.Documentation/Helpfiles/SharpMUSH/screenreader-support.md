<!-- help-article
{
  "corpus": "help",
  "id": "screenreader-support",
  "lookup": "screenreader support",
  "aliases": ["screen reader support"],
  "sections": [
    {
      "id": "use-the-layout-functions",
      "heading": "Use the layout functions",
      "lookup": "screenreader support layouts"
    },
    {
      "id": "pictures-and-drawings",
      "heading": "Pictures and drawings",
      "lookup": "screenreader support pictures"
    },
    {
      "id": "words-not-colour",
      "heading": "Words, not colour",
      "lookup": "screenreader support colour"
    },
    {
      "id": "checking-for-a-screen-reader",
      "heading": "Checking for a screen reader",
      "lookup": "screenreader support terminfo"
    }
  ]
}
-->
# Screen Reader Support

A screen reader reads the game out loud, line by line. Text drawn with characters (a border of dashes, a map, a logo, columns padded with spaces) is read as a run of punctuation, or as words from two columns run together. This topic is for builders and staff: how to write output that reads well to a screen reader and still looks right to everyone else. For what a player does, see [screenreader].

Most of it comes from saying what a thing is, rather than drawing it:

- [screenreader support layouts]: boxes, tables and fields with the layout functions.
- [screenreader support pictures]: pictures, logos and maps.
- [screenreader support colour]: meaning that colour alone carries.
- [screenreader support terminfo]: telling a screen reader's connection apart, for what nothing else covers.

## Use the layout functions

[BOX()], [FIELDS()], [DATATABLE()], [BULLETS()], [TREE()], [RULE()] and the other [LAYOUT FUNCTIONS] describe a layout once, and each client is sent it in its own form. A MU\* client that says it is a screen reader gets the content alone, in reading order: no borders, fields as `Label: value` lines, tree levels as indentation. The web portal sends its screen reader a real table, list or heading, and its Screen reader mode reads each field and list item as a phrase of its own.

The same layout built from [ALIGN()], [LJUST()] and [REPEAT()] is only spaces and characters. Nothing can tell a reader which part is a label and which a border.

Instead of this:

```sharp
> think repeat(-,30)%r[ljust(Sex:,10)]Male%r[ljust(Species:,10)]Human%r[repeat(-,30)]
```

write this:

```sharp
> think box(fields(,Sex,Male,Species,Human),Mannaz,30)
```

Everyone else sees a box titled Mannaz. In the portal, Screen reader mode reads it as "Mannaz. Sex: Male. Species: Human."

A divider with a title is [RULE()], not a line of `=` signs with the title in the middle. A screen reader hears the title alone.

The game's own listings (help, @mail, the admin lists) are built the same way. WHO stays one plain line per player, which already reads well.

## Pictures and drawings

[FIGURE()] takes a picture, a description and the text art a terminal shows instead. Every screen reader gets the description, in a client and in the portal, so give a real one:

```sharp
> think figure(/assets/logo.png,The SharpMUSH logo,== SharpMUSH ==)
```

An object's portrait or banner, in its `IMAGE` attribute, takes its description from ``IMAGE`ALT`` (see [IMAGE]); without one, the object's name is read.

A drawing that is not a picture, such as a room map or a logo of characters, needs a sentence of its own for a screen reader. See [screenreader support terminfo].

## Words, not colour

A screen reader does not hear colour. If red means out of character, say OOC as well:

```sharp
> think [ansi(r,OOC)] Back in ten minutes.
```

[NOTICE()] does this for system messages: every kind but `info` and `muted` puts a word before the text (`Done:`, `Warning:`, `Error:`), and a screen reader's connection is sent it as `JOBS error: ...`. Use it for a package's messages instead of a coloured tag alone.

A [GAUGE()] writes its figures after the bar, so a reader hears the number. Give it a label, such as `gauge(3,10,Health)`, so they know what it measures.

## Checking for a screen reader

[TERMINFO()] lists `screenreader` for a connection that a screen reader reads, whether its MU\* client said so through MTTS or its player turned on Screen reader mode in the web portal. Anyone's softcode may see it, as it can see `pueblo`. Use it for what the functions above cannot cover, such as a map:

```sharp
> &MAP here=%b%b%b%b%bPier%r%b%b%b%b%b%b|%rGate --- You
> &MAP`SAID here=The pier is north of you. The gate is west.
> @desc here=A cobbled square by the water.%r[if(member(terminfo(%#),screenreader),v(MAP`SAID),u(MAP))]
```

A player without a screen reader who types `look` sees:

```sharp
A cobbled square by the water.
     Pier
      |
Gate --- You
```

With a screen reader, they hear:

```sharp
A cobbled square by the water.
The pier is north of you. The gate is west.
```

Give the screen reader the same things, in words: the same exits, the same people, the same choices. Leave out only what draws.

::: seealso
- [screenreader]
- [LAYOUT FUNCTIONS]
- [FIGURE()]
- [NOTICE()]
- [TERMINFO()]
:::
