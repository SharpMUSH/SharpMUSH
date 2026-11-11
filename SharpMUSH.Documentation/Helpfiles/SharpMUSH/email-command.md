<!-- help-article
{
  "corpus": "help",
  "id": "email-command",
  "lookup": "@email",
  "aliases": [
    "@dialect_email"
  ],
  "sections": []
}
-->
# @email

`@email <address>[/<subject>]=<body>`

@email sends a plain-text email to `<address>` through the game's SMTP server. Without a subject it uses email_subject, or the game's name. Both sides are evaluated. The email goes out in the background, and you are told when it was sent or why it was not.

Only wizards may use @email. It is off until email_server and email_from are set, and the game sends no more than email_per_hour emails in an hour (see [@config net email]). The SMTP password is the `email.password` secret (see [@secret]).

@email is TinyMUX's command, reached under the `mux` dialect (see [compatibility config]); its own name everywhere is @dialect_email.

```sharp
> @email player@example.com/Greetings=Two and two is [add(2,2)].
@email: sent to player@example.com.
```

::: seealso
- [@secret]
- [@config net email]
:::
