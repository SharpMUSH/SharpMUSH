<!-- help-article
{
  "corpus": "help",
  "id": "pageformat-command",
  "lookup": "@pageformat",
  "aliases": [
    "@outpageformat"
  ],
  "sections": [
    {
      "id": "page-format-examples",
      "heading": "Page format examples",
      "lookup": "@pageformat page format examples"
    }
  ],
  "redirects": {
    "@pageformat2": "@pageformat page format examples",
    "@outpageformat2": "@pageformat page format examples"
  }
}
-->
# @pageformat

`@outpageformat <object>[=<message>]`<br>
`@pageformat <object>[=<message>]`

`@pageformat` changes the message seen by `<object>` when it receives a page.<br>
`@outpageformat` sets the message seen by `<object>` when it sends a page.

%0 will be set to the page message (not including :, ; or ").<br>
%1 will be set to ':' ';' or '"' for pose, semipose and normal page, respectively.<br>
%2 will be set to the alias of the pager, if any.<br>
%3 will be a space-separated list of recipient dbrefs.<br>
%4 will be set to the default message.


::: seealso
- [page]
- [speak()]
- [@chatformat]
- [@SPEECHMOD]
- [@message]
:::

## Page format examples

For simple page timestamps:
```sharp
> @pageformat me=\[[time()]\] %4
> @outpageformat me=\[[time()]\] %4
```

To obtain 'page_aliases' behavior:
```sharp
> @pageformat me=[setq(0,%n[if(%2,%b(%2))],1,switch(%3,%!,,itemize(iter(%3, name(##),%b,|),|)))][switch(%1,",%q0 pages[if(%q1,%b%q1)]: %0,:,From afar[if(%q1,%b(to %q1))]\, %q0 %0,From afar[if(%q1,%b(to %q1))]\, %q0%0)]
```

To obtain no 'page_aliases' behavior:
```sharp
> @pageformat me=[setq(1,switch(%3,%!,,itemize(iter(%3,name(##),%b,|),|)))][switch(%1,",%n pages[if(%q1,%b%q1)]: %0,:,From afar[if(%q1,%b(to %q1))]\, %n %0,From afar[if(%q1,%b(to %q1))]\, %n%0)]
```
