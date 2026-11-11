<!-- help-article
{
  "corpus": "help",
  "id": "lockencode-function",
  "lookup": "lockencode()",
  "aliases": [
    "LOCKDECODE()",
    "LOCKCHECK()",
    "lockencode",
    "lockdecode",
    "lockcheck"
  ],
  "sections": []
}
-->
# lockencode()

`lockencode(<key>)`<br>
`lockdecode(<encoded>)`<br>
`lockcheck(<encoded>, <object>[, <type>])`

These keep a lock key in a form that can be stored and passed around as text.

- lockencode() returns `<key>` in base64, with the names in it resolved to objects.
- lockdecode() returns the key an encoded one stands for, written as [LOCK()] writes one.
- lockcheck() returns 1 if `<object>` passes the encoded key and 0 if not. It tests the key as [TESTLOCK()] does. `<type>` is accepted and changes nothing.

An empty key, or text that is not an encoded key, gives `#-1 UNDEFINED KEY`. A key that names nothing gives `#-1 INVALID BOOLEXP`.

These functions come from RhostMUSH.

```sharp
> think lockencode(=#1)
PSMx
> think lockdecode(PSMx)
=#1
> think lockcheck(PSMx,#1)
1
```

::: seealso
- [TESTLOCK()]
- [LOCK()]
- [@LOCK]
:::
