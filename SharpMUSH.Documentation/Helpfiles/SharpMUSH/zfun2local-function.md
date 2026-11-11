<!-- help-article
{
  "corpus": "help",
  "id": "zfun2local-function",
  "lookup": "zfun2local()",
  "aliases": [
    "ZFUN2()",
    "ZFUNLOCAL()",
    "ZFUNDEFAULT()",
    "ZFUN2DEFAULT()",
    "ZFUNLDEFAULT()",
    "ZFUN2LDEFAULT()",
    "ZFUNEVAL()",
    "zfunlocal",
    "zfundefault",
    "zfun2default",
    "zfunldefault",
    "zfun2ldefault",
    "zfuneval"
  ],
  "sections": []
}
-->
# zfun2local()

`zfun2([<zone>/]<attribute>[, <arg0>[, ... , <arg29>]])`<br>
`zfunlocal([<zone>/]<attribute>[, <arg0>[, ...]])`<br>
`zfun2local([<zone>/]<attribute>[, <arg0>[, ...]])`<br>
`zfundefault([<zone>/]<attribute>, <default>[, <arg0>[, ...]])`<br>
`zfun2default([<zone>/]<attribute>, <default>[, <arg0>[, ...]])`<br>
`zfunldefault([<zone>/]<attribute>, <default>[, <arg0>[, ...]])`<br>
`zfun2ldefault([<zone>/]<attribute>, <default>[, <arg0>[, ...]])`<br>
`zfuneval([<zone>/]<attribute>/<role>[, <arg0>[, ...]])`

Each runs an attribute of one of your zones as the u() function of the same shape would: zfunlocal() as ulocal(), zfundefault() as udefault(), zfunldefault() as uldefault(), and zfuneval() as ueval(). Those with a 2 in the name run it as you, as u2() does, rather than as the zone.

`<zone>/` names one of the zones you are in; without it, the attribute is your first zone's. A zone you are not in, or none at all, gives `#-1 INVALID ZONE`.

These functions come from RhostMUSH.

```sharp
> &GREET #21=Hello, %0 from [name(me)].
> think zfun2(#21/greet,Ann) / [zfun(#21/greet,Ann)]
Hello, Ann from Wizard. / Hello, Ann from Zone B.
```

::: seealso
- [ZFUN()]
- [u()]
- [U2()]
- [zones]
:::
