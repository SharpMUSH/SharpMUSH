# who-where

`+who` and `+where` commands. Demonstrates the standard two-object shape:

- **`ww_global`** carries the `$command` patterns and `@parent`s to the
  function object so command code can call shared functions via `u()`.
- **`ww_functions`** is `no_command` and holds only callable attributes.

Both cross-object references use the `~internal` ref form
(`parent: ~ww_functions`, `u(~ww_functions/WW_FN_HEADER,...)`), which the
install engine substitutes with the real dbref at apply time.

The `WW_` convention prefix marks this package's attribute namespace.
