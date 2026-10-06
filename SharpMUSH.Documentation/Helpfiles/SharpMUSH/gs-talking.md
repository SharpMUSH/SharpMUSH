<!-- help-article
{
  "corpus": "help",
  "id": "gs-talking",
  "lookup": "gs talking",
  "aliases": [],
  "sections": [
    {
      "id": "posing-actions",
      "heading": "Posing actions",
      "lookup": "gs talking posing actions"
    },
    {
      "id": "talking-privately",
      "heading": "Talking privately",
      "lookup": "gs talking privately"
    }
  ],
  "redirects": {
    "GS TALKING2": "gs talking posing actions"
  }
}
-->
# gs talking

You can talk to others in the room with you (those listed in the 'Contents' of the room) in a number of ways. The easiest is to use the 'say' command.

For example, when you type:
  ```sharp
    say Hello!
  ```
you'll see:<br>
    You say, "Hello!"<br>
and everyone else in the room will see:<br>
    Lisa says, "Hello!"

You can abbreviate the command to just:
  ```sharp
    "Hello!
  ```
if you wish; it works exactly the same.

## Posing actions

You can also perform actions, using the 'pose' command, or ':' for short.<br>
For example:<br>
    pose waves!   or<br>
    :waves!<br>
will both show:<br>
    Lisa waves!<br>
to everyone in the room (including yourself). If you don't want a space after your name, use 'semipose' (or ';') instead:<br>
    ;'s waving!<br>
will show:<br>
    Lisa's waving!

If you don't want your name to be added at all, you can use the '`@emit`' command, or '\' for short:<br>
    `@emit` Smiling, Lisa waves, "Hello!"<br>
will show everyone in the room:<br>
    Smiling, Lisa waves, "Hello!"

However, make sure you include your name somewhere, so people know who's talking.

## Talking privately

To talk to someone who isn't in the room with you, use `page`: `page Lisa=Hi there!` sends Lisa a message anywhere in the game. To speak to one person in the room without the others hearing, use `whisper`: `whisper Lisa=Over here.` See [page] and [whisper].


::: seealso
- [gs chat]
- [say]
- [pose]
- [@emit]
- [page]
- [whisper]
:::
