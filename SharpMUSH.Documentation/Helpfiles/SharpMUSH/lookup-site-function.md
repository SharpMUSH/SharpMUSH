<!-- help-article
{
  "corpus": "help",
  "id": "lookup-site-function",
  "lookup": "lookup_site()",
  "aliases": [
    "lookup_site"
  ],
  "sections": []
}
-->
# lookup_site()

`lookup_site(<player>[, <port>[, <type>]])`

Returns the host names of `<player>`'s connections, separated by spaces. With `<port>`, it returns only the connection on that port. With `<type>` 1, it returns addresses instead of host names.

If the player is not connected and no `<port>` is given, it returns the host name or address the player last connected from. It returns `#-1` if there is nothing to return or you do not control `<player>`.

This function comes from RhostMUSH.

```sharp
> think lookup_site(me)
localhost
> think lookup_site(me,,1)
127.0.0.1
```

::: seealso
- [HOST()]
- [IPADDR()]
- [PORTS()]
:::
