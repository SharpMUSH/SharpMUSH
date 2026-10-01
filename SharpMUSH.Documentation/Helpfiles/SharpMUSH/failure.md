<!-- help-article
{
  "corpus": "help",
  "id": "failure",
  "lookup": "failure",
  "aliases": [],
  "sections": [
    {
      "id": "failure-attributes",
      "heading": "Failure attributes",
      "lookup": "failure attributes"
    }
  ],
  "redirects": {
    "failure2": "failure attributes"
  }
}
-->
# failure

FAILURE

A "failure" usually occurs when you try to do something that is governed by an `@lock` and you don't pass the lock. If you try to take a player or thing, or pass through an exit, and you don't pass its Basic `@lock`, you will set off their `@failure`/`@ofailure`/`@afailure` attributes. A few failures have special attributes, while others use `<locktype>`_LOCK`FAILURE, `<locktype>`_LOCK`OFAILURE and `<locktype>`_LOCK`AFAILURE attributes.

See [LOCKING]


**See Also:**
- [verbs]
- [LOCKING]
- [@afailure]
- [@aefail]
- [@lfail]

## Failure attributes

The following failures are defined in SharpMUSH:

Failure to...                                | Lock    | Attribute<br>
-------------------------------------------- | ------- | --------------------<br>
"get" a player/thing, pass through an exit, or "look" in a room | Basic   | `@failure`<br>
run an `$-command` on an object              | Command* | COMMAND_LOCK`FAILURE<br>
use `zwho()`                                 | Zone     | ZONE_LOCK`FAILURE<br>
leave your current location                  | Leave    | `@lfail`<br>
"take" from an object                        | Take     | TAKE_LOCK`FAILURE<br>
"drop" a thing, or drop something in a room  | Drop     | DROP_LOCK`FAILURE<br>
enter an object                             | Enter    | `@efail`<br>
"follow" an object                           | Follow   | FOLLOW_LOCK`FAILURE<br>
"give" an object away                        | Give     | GIVE_LOCK`FAILURE<br>
"give" money to or "buy" from an object      | Pay      | PAY_LOCK`FAILURE<br>
"`@chzone`" something to a zone              | Chzone   | CHZONE_LOCK`FAILURE<br>
"use" an object                              | Use      | `@ufail`<br>
speak via say/pose/@*emit/teach in a room    | Speech   | SPEECH_LOCK`FAILURE<br>
"page" or "`@pemit`" to an object            | Page     | PAGE_LOCK`FAILURE**<br>
"`@mail`" a player                           | Mail     | MAIL_LOCK`FAILURE

`*` The Use lock can also prevent you from running an `$-command`, but it will still trigger COMMAND_LOCK`FAILURE.<br>
`**` `@haven` or `@away` will also be shown on failure to "page", if set
