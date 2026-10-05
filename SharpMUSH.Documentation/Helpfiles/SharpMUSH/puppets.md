<!-- help-article
{
  "corpus": "help",
  "id": "puppets",
  "lookup": "puppets",
  "aliases": [],
  "sections": [
    {
      "id": "puppet-example",
      "heading": "Puppet example",
      "lookup": "puppets puppet example"
    }
  ],
  "redirects": {
    "PUPPETS2": "puppets puppet example"
  }
}
-->
# puppets

A thing is turned into a puppet by setting the PUPPET flag on it. A puppet object is an extension of its owner and relays everything it sees and hears to its owner, except if it is in the same room as the owner (a puppet with the VERBOSE flag will relay even if it's in the same room). Things relayed by the puppet will be prefixed by the name of the puppet.

Puppets are useful for keeping track of what is going on in two rooms at once, as extensions of a player (such as a pet, for example), or for testing code.

You can control your puppets using the `@force` command. It is important to remember the DBREF numbers of your puppets so you can control them even if they are not in the same room with you. You can also have your puppets follow you by using the 'follow' command.

## Puppet example

An example of a puppet:

```sharp
  > @create Punch
  Created: Object #18.
  > drop Punch
  Dropped.
  > @set punch=puppet
  Punch is now listening.
  Punch - PUPPET set.
  > @force punch=go north
  Punch has left.
  Punch> The Finishing Place
  Punch>
  Punch> Obvious exits:
  Punch> Door \<S\>
  > @force #18=:waves hello
  Punch> Punch waves hello
  > #18 say Hello.
  Punch> You say, "Hello."
```

To have an object relay things it hears to players other than its owner, use `@forwardlist`.

::: seealso
- [PUPPET]
- [@force]
- [database]
:::
