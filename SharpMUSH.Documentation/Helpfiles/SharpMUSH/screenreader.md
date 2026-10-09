<!-- help-article
{
  "corpus": "help",
  "id": "screenreader",
  "lookup": "screenreader",
  "aliases": ["screen reader", "screen readers"],
  "sections": [
    {
      "id": "in-a-mu-client",
      "heading": "In a MU* client",
      "lookup": "screenreader client"
    },
    {
      "id": "in-the-web-portal",
      "heading": "In the web portal",
      "lookup": "screenreader portal"
    }
  ]
}
-->
# Screen Readers

SharpMUSH can be played with a screen reader, in a MU\* client or in the web portal. Once the game knows a screen reader is reading, it sends text that reads well aloud:

- no colour codes, which some screen readers speak
- no box drawing: a box, a table or a set of fields is sent one item after another, in reading order
- a picture as its description
- a system message with its kind in words, such as `JOBS error: Only a Job Admin can do that.`

To check that the game knows, type:

```sharp
> think terminfo(me)
```

The word `screenreader` is in the list when it does.

- [screenreader client]: telling the game from a MU\* client.
- [screenreader portal]: Screen reader mode in the web portal, and its keys.

Builders and staff: see [screenreader support] for making a game read well.

## In a MU* client

Your client tells the game it is a screen reader through MTTS, the standard way MU\* clients describe themselves. Look in your client's accessibility settings for an option to tell games you use a screen reader, turn it on, and connect again.

With it on, the game sends your connection plain text, with the layout in reading order. Command links are left out too. To have them back, if your client can follow them:

```sharp
> SOCKSET commandlinks=on
```

If your client has no such option, `SOCKSET colorstyle=plain` still turns the colour codes off (see [colorstyle]), but boxes and tables are drawn as they are for everyone else.

## In the web portal

The portal works with NVDA, JAWS, VoiceOver and TalkBack. Browsers do not tell a page that a screen reader is running, so the portal asks once: open Play, open Terminal settings (the button above the terminal on the right), and tick Screen reader mode. The choice is kept in that browser, for every character you play in it.

With it on:

- New lines are read out as they arrive, without moving your focus. Your own commands are not read back.
- Alt and a number (Option on a Mac), pressed in the command box, reads a recent line again: Alt+1 the newest, Alt+2 the one before, up to Alt+0 for the tenth. What you have typed stays in the box.
- A line is read as words: a picture as its description, a divider as its title, and a line of dashes or box characters not at all.
- The exit letter keys are turned off, so a stray letter cannot walk your character out of the room.
- The game is told, so `terminfo(me)` lists `screenreader`.

Every page starts with two links, Skip to main content and Skip to the text box. Each page has one main heading, and on Play the cards beside the terminal, such as Here and Exits, are headings too.

::: seealso
- [screenreader support]
- [terminfo()]
- [@SOCKSET]
:::
