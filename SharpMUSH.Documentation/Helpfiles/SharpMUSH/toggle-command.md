<!-- help-article
{
  "corpus": "help",
  "id": "toggle-command",
  "lookup": "@toggle",
  "aliases": [],
  "sections": []
}
-->
# @toggle

`@toggle <object>=[!]<toggle> [[!]<toggle> ...]`<br>
`@toggle/check [<object>]`<br>
`@toggle/clear <object>`

@toggle sets each toggle named on `<object>`, or clears it when the name starts with `!`, as `@set` does with flags (see [toggles]). A name that is no toggle is reported and the rest are still set.

`/check`, or `@toggle <object>` with no list, lists the toggles `<object>` has that you may see; `/check` with no object lists your own. `/clear` takes off every toggle of `<object>` you may see. Each form needs control of `<object>`.

@toggle comes from RhostMUSH.

```sharp
> @create Box
> @toggle Box=MONITOR
> @toggle/check Box
Toggles: MONITOR
> @toggle/clear Box
@toggle: all toggles cleared.
```

::: seealso
- [toggles]
- [toggle()]
- [@set]
:::
