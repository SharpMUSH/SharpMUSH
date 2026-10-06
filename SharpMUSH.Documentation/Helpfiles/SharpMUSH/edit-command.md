<!-- help-article
{
  "corpus": "help",
  "id": "edit-command",
  "lookup": "@edit",
  "aliases": [
    "@gedit"
  ],
  "sections": [
    {
      "id": "regular-expression-replacement",
      "heading": "Regular expression replacement",
      "lookup": "@edit regular expression replacement"
    }
  ],
  "redirects": {
    "@edit2": "@edit regular expression replacement"
  }
}
-->
# @edit

`@edit[/first][/check][/quiet] <object>/<attributes>=<search>, <replace>`<br>
`@edit[/check][/quiet] <object>/<attributes>=$, <string to append>`<br>
`@edit[/check][/quiet] <object>/<attributes>=^, <string to prepend>`

This command allows you to edit the contents of attributes, without having to retype the entire attribute.

All the attributes on `<object>` whose names match the wildcard pattern `<attributes>` will be searched for the string `<search>`, and each occurrence of it will be replaced with the string `<replace>`.

If `<search>` is "$", then `<replace>` will be added to the end of the attributes. When `<search>` is "^", `<replace>` will be added to the beginning of the attributes. (If you need to replace a single $ or ^, consider using @edit/regexp (see help @edit regular expression replacement) or the edit() function.)

If the `/first` switch is given, only the first occurrence of `<search>` in each attribute is replaced. If the `/check` switch is given, the attributes are not altered, you'll just be shown what would be changed (with the changes ansi-highlighted).

If the `/quiet` switch is given, you won't be shown the modified text, you'll just be told how many of the matching attributes were/weren't edited. Useful when edited a lot of large/spammy attributes.

`<search>` and `<replace>` are not evaluated, so you don't need to escape special characters. If either contains commas, however, you may need to wrap the string in {curly braces}.

## Regular expression replacement

`@edit/regexp[/all][/nocase][/check][/quiet] <object>/<attributes>=<regexp>,<replace>`

When the `/regexp` switch is given, @edit performs a regular expression replacement, similar to the regedit() function. Normally, only the first match per attribute will be replaced; if the `/all` switch is given, all possible matches in each attribute are replaced (as per regeditall()).

Regexp matches are case-sensitive by default, but the `/nocase` switch can be given to make them case-insensitive (as per regediti()/regeditalli()).

The `/check` and `/quiet` switches work the same as for non-regexp @edits.

Note that, unlike normal @edits, the `<replace>` for an `@edit/regexp` WILL be evaluated, once for each replacement made, with the $0 token being replaced with the overall matching text, $1 with the first subexpression, and so on. Named subexpressions are also possible via `$<name>`.

### Example
```sharp
> &foo me=Block of text/Wed Feb 22 22:54:02 2012/#10010
> @edit/regexp me/foo=^(.+)/([^/]+)/(#[0-9]+(?::[0-9]+)?)$, ucstr($1) -- [convtime($2)] -- [name($3)]
FOO - Set: BLOCK OF TEXT -- 1329951242 -- Minion
```
Replace a literal '^' with 'v'
```sharp
> @edit/regexp me/bar=\^, v
```


::: seealso
- [EDIT()]
- [REGEDIT()]
- [attributes]
- [WILDCARDS]
:::
