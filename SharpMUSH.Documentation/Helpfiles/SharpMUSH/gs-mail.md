<!-- help-article
{
  "corpus": "help",
  "id": "gs-mail",
  "lookup": "gs mail",
  "aliases": [],
  "sections": [
    {
      "id": "sending-mail",
      "heading": "Sending mail",
      "lookup": "gs mail sending mail"
    }
  ],
  "redirects": {
    "GS MAIL2": "gs mail sending mail"
  }
}
-->
# gs mail

SharpMUSH has a built-in mail system that lets you send messages to players, even if they aren't online. You can keep mail you receive for as long as you like, and re-read it any time.

To list all the messages you've received, type '`@mail`'. You'll see something like:
```text
-------------------------  MAIL (folder  0)  ----------------------------
[-----]  0:1    One           Welcome!                   Wed Dec 08 09:57
[-----]  0:2   *Mike          Example Mail               Sat Dec 11 07:55
-------------------------------------------------------------------------
```
The number after the ':' is the message number; to read that message, type '`@mail` `<number>`'.

## Sending mail

To send mail to someone, type:<br>
`@mail <recipients>=[<subject>/]<message>`

You can send a message to more than one person at a time, just include the names of all the people you want to send to in `<recipients>`. The `<subject>` is optional. For example:

```sharp
@mail qa'toq anne=Test/Hi! This is a test message!
```

You can do other, slightly more complex things with the mail system, too, like filing your messages into different folders. See [@mail] for more information.


::: seealso
- [MAIL]
:::
