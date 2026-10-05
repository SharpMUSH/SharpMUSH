<!-- help-article
{
  "corpus": "help",
  "id": "locktypes",
  "lookup": "locktypes",
  "aliases": [
    "LOCKLIST",
    "LOCK TYPES",
    "LOCK LIST"
  ],
  "sections": [
    {
      "id": "additional-lock-types",
      "heading": "Additional lock types",
      "lookup": "locktypes additional lock types"
    }
  ],
  "redirects": {
    "LOCK TYPES2": "locktypes additional lock types",
    "LOCKTYPES2": "locktypes additional lock types"
  }
}
-->
# locktypes

These are the standard lock types supported by SharpMUSH. For more detailed information about any lock type, see `[@lock/<lock>]`.

Standard Lock Types:
- `@lock/basic` - Who can pick up the player/thing, or go through the exit.
- `@lock/enter` - Who can enter the player/object (aka @elock)
- `@lock/teleport` - Who can teleport to the room
- `@lock/use` - Who can use the object (aka @ulock)
- `@lock/page` - Who can page/@pemit the player
- `@lock/zone` - Who can control objects on this zone
- `@lock/parent` - Who can @parent something to this object/room
- `@lock/link` - Who can @link something to this object/room or who can @link this unlinked exit.
- `@lock/open` - Who can @open an exit from this room
- `@lock/mail` - Who can @mail the player
- `@lock/user:<name>` - User-defined. No built-in function of this lock, but users can test it with elock()


::: seealso
- [locktypes additional lock types]
:::

## Additional lock types

More standard lock types:

- `@lock/speech` - Who can speak/pose/emit in this room
- `@lock/listen` - Who can trigger my @ahear/^-pattern actions
- `@lock/command` - Who can trigger my $-pattern commands
- `@lock/leave` - Who can leave this object (or room, via exits/@tel)
- `@lock/drop` - Who can drop this object
- `@lock/dropin` - Who can drop objects into this location.
- `@lock/give` - Who can give this object
- `@lock/from` - Who can give things to this object
- `@lock/pay` - Who can give pennies to/buy from this object
- `@lock/receive` - What things can be given to this object
- `@lock/follow` - Who can follow this object
- `@lock/examine` - Who can examine this object if it's VISUAL
- `@lock/chzone` - Who can @chzone to this object if it's a ZMO
- `@lock/forward` - Who can @forwardlist a message to this object 
- `@lock/filter` - Controls if the message %0 should be filtered
- `@lock/infilter` - Controls if the message %0 should be infiltered
- `@lock/control` - Who can control this object (only if set; non-player)
- `@lock/dropto` - Who can trigger this container's drop-to.
- `@lock/destroy` - Who can destroy this object if it's DESTROY_OK
- `@lock/interact` - Who can send sound (say/pose/emit/etc) to this object
- `@lock/take` - Who can get things contained in this object
- `@lock/mailforward` - Who can forward mail to this object via @mailforward
- `@lock/chown` - Who can @chown this CHOWN_OK object?


::: seealso
- [LOCKING]
- [@lset]
- [@CHANNEL CLOCK]
- [failure]
:::
