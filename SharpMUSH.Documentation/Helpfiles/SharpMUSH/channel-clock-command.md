<!-- help-article
{
  "corpus": "help",
  "id": "channel-clock-command",
  "lookup": "@channel clock",
  "aliases": ["@channel/clock", "@clock"],
  "sections": [
    {"id": "locks", "heading": "The five locks", "lookup": "@channel clock locks"},
    {"id": "examples", "heading": "Examples", "lookup": "@channel clock examples"}
  ]
}
-->
# @channel clock

`@clock/join <channel>[=<lock>]`<br>
`@clock/speak <channel>[=<lock>]`<br>
`@clock/see <channel>[=<lock>]`<br>
`@clock/hide <channel>[=<lock>]`<br>
`@clock/mod <channel>[=<lock>]`

Channel locks are set with `@clock`, which is its own command; there is no `@channel/clock` switch. Each switch names the one lock it sets, and omitting *<lock>* removes that lock. With no switch at all, `@clock` sets the join lock. See [lock keys] for what may go in a *<lock>*.

- [@channel clock locks]: the five locks, and who may set them
- [@channel clock examples]: setting and removing locks

## The five locks

There are five locks:
- **join**: Restricts who can join the channel
- **speak**: Restricts who can speak on the channel
- **see**: Restricts who can see the channel on `@channel/list`
- **hide**: Restricts `@channel/hide` on a channel that is `hide_ok`
- **mod**: Restricts who can modify the channel. Passing the mod lock lets you do anything to a channel short of deleting it

A lock is evaluated against the object trying to pass it, exactly as if it had been set on that object.

You may set a channel's locks if you own it, if you pass its mod lock, or if you are a wizard. A new channel starts with none of the five set, and an unset lock is one everybody passes.

## Examples

```sharp
@clock/join Public=flag^wizard
@clock/speak Public=!flag^gagged
@clock/mod Secret=#123
@clock/speak Public=
```

::: seealso
- [LOCKING]
- [lock keys]
- [@CHANNEL PRIVS]
:::
