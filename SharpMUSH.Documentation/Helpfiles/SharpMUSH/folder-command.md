<!-- help-article
{
  "corpus": "help",
  "id": "folder-command",
  "lookup": "@folder",
  "aliases": [
    "@mux_folder"
  ],
  "sections": []
}
-->
# @folder

`@folder [<folder>]`<br>
`@folder <folder>=<messages>`<br>
`@folder/read <folder>=<messages>`<br>
`@folder/list <folder>[=<messages>]`<br>
`@folder/file <messages>=<folder>`<br>
`@folder/set <folder>[=<name>]`

@folder is TinyMUX's command for @mail's folders.

- With nothing, it lists your folders, as @mail/folder does.
- `@folder <folder>` makes `<folder>` your current one.
- `@folder <folder>=<messages>` and /read read messages in that folder without making it current.
- /list lists the messages in a folder, all of them unless you name some.
- /file moves messages from the current folder into another, as @mail/file does.
- /set makes a folder current, or with a name, names it.

@folder is reached under the TinyMUX dialect (see [compatibility config]); its own name everywhere is @mux_folder. Without the dialect, @fo still abbreviates @force.

```sharp
> @folder/set 1=Archive
> @folder/file 3=1
> @folder 1=1
```

::: seealso
- [@mail]
- [mail-folders]
:::
