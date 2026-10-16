<!-- help-article
{
  "corpus": "help",
  "id": "plugin-command",
  "lookup": "@plugin",
  "aliases": [],
  "sections": [
    {"id": "disable-enable", "heading": "Turning a plugin off and on", "lookup": "@plugin disable and enable", "aliases": ["@plugin/disable", "@plugin/enable"]}
  ]
}
-->
# @plugin

`@plugin[/list]`<br>
`@plugin <plugin>`<br>
`@plugin/enable <plugin>`<br>
`@plugin/disable <plugin>`

Needs the `packages.admin` permission. A plugin is compiled C# that adds commands, functions, flags and portal pages to the server; the Scene System is one, shipped with the server. Plugins installed from a plugin package, from a remote or uploaded in the portal's Packages page, sit beside the world on the data volume, so they survive an update of the server's image.

`@plugin` lists every plugin the server found: its version, whether it shipped with the server or came from a package, and its state. `@plugin <plugin>` shows one, with what it adds, which packages need it, and why it did not start if it failed.

- [@plugin disable and enable]: what turning a plugin off removes, and when it stops

## Turning a plugin off and on

`@plugin/disable <plugin>`<br>
`@plugin/enable <plugin>`

`@plugin/disable <plugin>` turns a plugin off. The packages that need it are uninstalled first, after a backup: those that name it under `plugins:` in their package.yaml, and the packages that depend on those. While a plugin is not running, a package that names it cannot be installed, and a package that shipped with the server is not reinstalled. A plugin that only adds commands and functions stops at once. Any other plugin stops at the next restart (`@shutdown/reboot`), because what it registered at start-up stays until then.

`@plugin/enable <plugin>` turns it on again. It starts at the next restart, and the packages that shipped with it are reinstalled then.

### Examples

```sharp
> @plugin
> @plugin/disable scene
Uninstalled scene. Scene System is off. Scene System stops when the server restarts.
> @shutdown/reboot
```

::: seealso
- [@shutdown]
- [@package]
- [plugin help]
:::
