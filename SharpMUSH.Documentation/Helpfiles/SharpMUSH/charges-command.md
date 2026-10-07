<!-- help-article
{
  "corpus": "help",
  "id": "charges-command",
  "lookup": "@charges",
  "aliases": [
    "@runout"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Limited use example",
      "lookup": "@charges examples"
    }
  ],
  "redirects": {
    "charges2": "@charges examples",
    "runout2": "@charges examples"
  }
}
-->
# @charges

`@charges <object>[=<integer>]`<br>
`@runout <object>[=<action list>]`

These attributes can limit how many times an object can be successfully "use"d. When you "use" an object with a CHARGES attribute set, the object's AUSE attribute is only triggered if CHARGES is a positive integer. When CHARGES is less than 1 (or not a number), the object's RUNOUT attribute is triggered instead.

When the CHARGES attribute is present and AUSE is triggered, the value of the CHARGES attribute is automatically decreased by 1. When no CHARGES attribute is set, AUSE is always triggered.


::: seealso
- [use]
- [@ause]
- [action lists]
:::

## Limited use example

```sharp
> @create Revolver
> @use Revolver=You pull the trigger.
> @ouse Revolver=pulls the trigger.
> @charges Revolver=6
> @ause Revolver=POSE fires into the air.
> @runout Revolver=POSE clicks, but is out of bullets.
```

```sharp
> use revolver
You pull the trigger.
Revolver fires into the air.
> ex revolver/charges
CHARGES [#6$]: 5
```

The next 5 "use revolver"s work the same way, decrementing CHARGES each time.

```sharp
> ex revolver/charges
CHARGES [#6$]: 0
> use revolver
You pull the trigger.
Revolver clicks, but is out of bullets.
> ex revolver/charges
CHARGES [#6$]: 0
```
