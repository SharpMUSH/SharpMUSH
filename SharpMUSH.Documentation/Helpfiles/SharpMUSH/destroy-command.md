<!-- help-article
{
  "corpus": "help",
  "id": "destroy-command",
  "lookup": "@destroy",
  "aliases": [
    "@recycle",
    "@nuke"
  ],
  "sections": [
    {
      "id": "destruction-lifecycle",
      "heading": "Destruction lifecycle",
      "lookup": "@destroy destruction lifecycle",
      "aliases": [
        "DESTRUCTION"
      ]
    }
  ],
  "redirects": {
    "@destroy2": "@destroy destruction lifecycle"
  }
}
-->
# @destroy

`@destroy[/override] <object>` or `@recycle[/override] <object>`<br>
`@nuke <object>`

The @destroy command marks `<object>` for destruction, or destroys `<object>` instantly if it was already marked for destruction. You must either control `<object>`, control its source or destination room (for exits), or it must be set DESTROY_OK and you must pass its @lock/destroy.

To destroy objects set SAFE, you must use `@destroy/override` or @nuke. If the really_safe @config option is on, even @nuke can't destroy SAFE objects, and you must clear the SAFE flag first.

@recycle is an alias for @destroy. Some MUSHes disable @destroy and only use @recycle, to avoid players mistyping. @nuke is an alias for `@destroy/override`.

::: seealso
- [@undestroy]
- [@create]
- [@dig]
- [@open]
- [DESTROY_OK]
- [SAFE]
:::

## Destruction lifecycle

When an object is marked for destruction, the GOING flag is set on it and its @adestroy attribute is triggered (if the 'adestroy' @config option is true). If `<object>` is a room, all the exits in the room are marked for destruction as well. If `<object>` is a player, and the @config option destroy_possessions is on, everything he owns is marked for destruction as well. (If really_safe is also on, his SAFE objects are spared.)

The MUSH checks for GOING objects every ten minutes or so (see '@config purge_interval'); each one is set with the GOING_TWICE flag, and will be destroyed totally on the next cycle. You can save it from destruction during this period using the @undestroy command, or @destroy it again to destroy it instantly. The GOING and GOING_TWICE flags cannot be set or removed manually.

When an object is destroyed, any commands, @waits and semaphores it has queued are drained, and the object stops counting against its owner's used building quota. SharpMUSH has no monetary creation deposit to refund. The ``OBJECT`DESTROY`` event is also queued.

Players can only be @destroyed when they are not connected, and even then can only be destroyed by a Wizard player. If the destroy_possessions @config option is on, anything the player owns is @destroyed. If the really_safe option is also on, his SAFE possessions are spared. Any objects he owns which aren't destroyed are @chown'd to the Probate player (as per '@config probate_judge'), as are any @channels and @feed kinds the player owned.


::: seealso
- [SAFE]
- [EVENTS]
:::
