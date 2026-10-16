# who-where

`+who` and `+where` commands. Demonstrates the standard two-object shape:

- **`ww_global`** carries the `$command` patterns and `@parent`s to the
  function object so command code can call shared functions via `u()`.
- **`ww_functions`** is `no_command` and holds only callable attributes.

Both cross-object references use the internal ref form
(`parent: "{{ww_functions}}"`, ``u({{ww_functions}}/FUN`WHO,...)``), which the
install engine substitutes with the real dbref at apply time.

Attributes are named as trees: ``CMD`WHO`` and ``CMD`WHERE`SWITCH`` for the
commands, ``FUN`<section>`<name>`` for functions (``FUN`WHO`ROW``,
``FUN`ROOM`GROUPS``), ``DATA`PART`PLAYERS`` and ``DATA`PART`ROOMS`` for how many
players or locations each part of a screen sends, and ``DATA`CONFIG`` for the
`+config` settings. A `+where` switch names the screen it runs
(``FUN`WHERE`<SWITCH>``), and each screen's parts are drawn by
``FUN`<SCREEN>`PART``.

Another package adds to `+who` without editing this one: it attaches
``FILTER`WHO`STATUS`<NAME>`` to `ww_functions`, and
``u({{who-where/ww_functions}}/FUN`FILTER`APPLY,WHO`STATUS,,<player>)`` gives
what every such filter makes of a player.

## Command palette

`+who` and `+where` are palette staples (``PALETTE`WHO`` and ``PALETTE`WHERE`` on `ww_global`): the portal's Ctrl+K lists them under Always.
