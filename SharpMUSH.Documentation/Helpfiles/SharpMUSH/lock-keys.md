<!-- help-article
{
  "corpus": "help",
  "id": "lock-keys",
  "lookup": "lock keys",
  "aliases": [],
  "sections": [
    {
      "id": "combining-lock-keys",
      "heading": "Combining lock keys",
      "lookup": "lock keys combining lock keys",
      "aliases": [
        "@LOCK-COMPLEX"
      ]
    }
  ],
  "redirects": {
    "LOCK KEYS2": "lock keys combining lock keys"
  }
}
-->
# Lock Keys

There are many key types, and it is also possible to form more complex locks by using boolean symbols and grouping. See [lock keys combining lock keys] for examples.

The types of keys are outlined below. Detailed help for each is available by typing `[@lock-<key>]`, replacing *<key>* with the word on the left.

- **Simple** - Always true, always false, or locking to a specific object.
- **Name** - Check the name of the object attempting to pass the lock.
- **Owner** - Lock to objects owned by the owner of an object.
- **Carry** - Lock to someone carrying an object, such as a key.
- **Indirect** - Use the result of another @lock.
- **Attribute** - Check an attribute on the object trying to pass the lock.
- **Evaluation** - Evaluate an attribute on the object the lock is on.
- **Bit** - Check for a flag, type, power, role, permission, or channel membership.
- **Dbreflist** - Check if the dbref of the object trying to pass the lock is in a list set in an attribute.
- **Host** - Check for players connecting from a particular host/ip.

You can negate lock keys, and combine multiple keys, as explained in [lock keys combining lock keys].

## Combining lock keys

A lock key can be negated by prefixing the key with an "!". For example:

```sharp
> @lock North=flag^wizard
> @lock South=!flag^wizard
```

only lets those with the Wizard flag pass through the North exit, while only allowing those who do NOT have the Wizard flag to go South.

You can combine keys, either allowing someone to pass a lock if they pass any of the keys given, or requiring that they pass all of the keys, using the "|" (or) and "&" (and) symbols. For example:

```sharp
> @lock OOC Room= status:OOC | power^guest
```

locks the exit "OOC Room" so that only those with their STATUS attribute set to "OOC", or those with the Guest @power, can pass, while

```sharp
> @lock Men's Room= Sex:Male & +Bathroom Key
```

only allows those with their @sex set to Male who are carrying a "Bathroom Key" object to pass.

You can group together different sets of keys by enclosing each group in parenthesis "()". For instance,

```sharp
> @lock Entrance=!type^player | (type^player & !flag^unregistered)
```

allows non-players to pass, or players who do not have the "unregistered" flag set.


::: seealso
- [LOCKING]
- [locktypes]
- [@CHANNEL CLOCK]
- [OBJID()]
:::
