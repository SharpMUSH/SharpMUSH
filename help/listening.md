<!-- help-article
{
  "corpus": "help",
  "id": "listening",
  "lookup": "listening",
  "aliases": [],
  "sections": [
    {
      "id": "listen-patterns",
      "heading": "Listen patterns",
      "lookup": "listening listen patterns",
      "aliases": [
        "^",
        "^-LISTENS"
      ]
    },
    {
      "id": "listener-flags-and-triggers",
      "heading": "Listener flags and triggers",
      "lookup": "listening listener flags and triggers"
    }
  ],
  "redirects": {
    "LISTENING2": "listening listen patterns",
    "LISTENING3": "listening listener flags and triggers"
  }
}
-->
# Listening

There are two basic ways to trigger action on the MUSH. The basic way is to type in commands such as 'look' or '`@emit`'. These commands are not seen or heard by other players, although the results of the commands may be.

The other way is to "listen" for something said/emitted in your hearing. There are two ways to listen for something in a room. The easiest way is to use a combination of `@listen` and `@ahear`/`@aahear`/`@amhear`.

For example:
```sharp
    > @listen Welcome Mat=* has arrived.
    > @ahear Welcome Mat="Welcome, %n!
    Breaker has arrived.
    Welcome Mat says, "Welcome, Breaker!"
```

To trigger an object's `@listen`, you must pass its `@lock`/listen.

## Listen patterns

If you need an object to "listen" for more than one pattern, you can also use ^-patterns. These work similar to user-defined commands, using ^ instead of $. An object must be set MONITOR to have ^-patterns activated.

Syntax:  &`<attribute>` `<object>` = ^`<pattern>`:`<action list>`

For example:
```sharp
    > @set Welcome Mat = MONITOR
    > &greet Welcome Mat=^* has arrived.:"Welcome, %n!
    > &goodbye Welcome Mat=^* has left.:POSE says as %n leaves, "Bye!"
    Grimlock has arrived.
    Welcome Mat says, "Welcome, Grimlock!"
    Grimlock has left.
    Welcome Mat says as Grimlock leaves, "Bye!"
```

Such attributes can also be `@triggered` as if the ^`<pattern>`: did not exist.

In order to trigger an object's ^-listen patterns, you must pass BOTH its `@lock`/use and its `@lock`/listen.

## Listener flags and triggers

By default, ^-patterns work like `@ahear`. To have them work like `@amhear`, set the AMHEAR attribute flag on the attribute; to have them work like `@aahear`, set the AAHEAR attribute flag on the attribute (Note that the triggering object is whatever happens to be %#, so, for example, when you `@set` an object MONITOR, you are %# with regard to the "Object is now listening" message, and this message can be picked up with an ^-pattern.)

Additionally, unlike `$-commands`, ^-patterns are NOT inherited via `@parent`, unless the LISTEN_PARENT flag is set on the listener. `@listen` is never inherited.

Listen patterns are checked after the object's normal `@listen` attribute.

::: seealso
- [@listen]
- [@ahear]: `@ahear`, `@amhear`, and `@aahear` provide the three listener reaction attributes.
- [MONITOR]
- `[LISTEN_PARENT]`
- [$-commands]
- [interiors]
:::
