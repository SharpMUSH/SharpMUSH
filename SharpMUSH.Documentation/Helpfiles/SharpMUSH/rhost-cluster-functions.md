<!-- help-article
{
  "corpus": "help",
  "id": "rhost-cluster-functions",
  "lookup": "rhostmush clusters",
  "aliases": [
    "cluster functions",
    "clusters"
  ],
  "sections": [
    {
      "id": "iscluster",
      "heading": "iscluster()",
      "lookup": "iscluster()",
      "aliases": []
    },
    {
      "id": "cluster-get",
      "heading": "cluster_get()",
      "lookup": "cluster_get()",
      "aliases": [
        "cluster_get_eval()",
        "cluster_xget()"
      ]
    },
    {
      "id": "cluster-default",
      "heading": "cluster_default()",
      "lookup": "cluster_default()",
      "aliases": [
        "cluster_edefault()"
      ]
    },
    {
      "id": "cluster-u",
      "heading": "cluster_u()",
      "lookup": "cluster_u()",
      "aliases": [
        "cluster_u2()",
        "cluster_ulocal()",
        "cluster_u2local()",
        "cluster_udefault()",
        "cluster_uldefault()",
        "cluster_u2default()",
        "cluster_u2ldefault()",
        "cluster_ueval()"
      ]
    },
    {
      "id": "cluster-hasattr",
      "heading": "cluster_hasattr()",
      "lookup": "cluster_hasattr()",
      "aliases": []
    },
    {
      "id": "cluster-lattr",
      "heading": "cluster_lattr()",
      "lookup": "cluster_lattr()",
      "aliases": [
        "cluster_attrcnt()",
        "cluster_vattrcnt()"
      ]
    },
    {
      "id": "cluster-flags",
      "heading": "cluster_flags()",
      "lookup": "cluster_flags()",
      "aliases": [
        "cluster_hasflag()"
      ]
    },
    {
      "id": "cluster-grep",
      "heading": "cluster_grep()",
      "lookup": "cluster_grep()",
      "aliases": [
        "cluster_regrep()",
        "cluster_regrepi()"
      ]
    },
    {
      "id": "cluster-set",
      "heading": "cluster_set()",
      "lookup": "cluster_set()",
      "aliases": [
        "cluster_wipe()"
      ]
    },
    {
      "id": "cluster-stats",
      "heading": "cluster_stats()",
      "lookup": "cluster_stats()",
      "aliases": []
    },
    {
      "id": "cluster-add",
      "heading": "cluster_add()",
      "lookup": "cluster_add()",
      "aliases": []
    }
  ]
}
-->
# RhostMUSH Clusters

RhostMUSH can spread one set of attributes over several objects, called a cluster, to get past its limit on attributes per object. SharpMUSH has no such limit to get past: an object holds up to 100,000 attributes. A RhostMUSH import merges each cluster into the object it was made on, which takes the others' attributes, locks, and flags other than those that grant privilege. The numbers the other objects had keep naming the merged object until a new object takes one, so `get(#456/VA)` still works.

The cluster functions work on that one object. Each gives `#-1 NO SUCH CLUSTER` when its object is not a cluster. In the examples, #123 is a cluster that #456 was merged into. It has VA `This is a test`, VB `This is another test.`, VC `Test Test Test whisooo!` and FN `Got %0 and %1`.

## iscluster()

`iscluster(<object>)`

1 if *\<object\>* is a cluster, 0 if not. An object you cannot find or do not control gives `#-1`.

```sharp
> think [iscluster(#456)] [iscluster(me)]
1 0
```

## cluster_get()

`cluster_get(<object>/<attribute>)`<br>
`cluster_get_eval(<object>/<attribute>)`<br>
`cluster_xget(<object>, <attribute>)`

[get()], [get_eval()] and [xget()] on the cluster.

```sharp
> think cluster_get(#456/VA)
This is a test
```

## cluster_default()

`cluster_default(<object>/<attribute>, <default>)`<br>
`cluster_edefault(<object>/<attribute>, <default>)`

[default()] and [edefault()] on the cluster.

```sharp
> think cluster_default(#123/NOPE,none)
none
```

## cluster_u()

`cluster_u([<object>/]<attribute>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_u2([<object>/]<attribute>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_ulocal([<object>/]<attribute>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_u2local([<object>/]<attribute>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_udefault([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_uldefault([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_u2default([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_u2ldefault([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`<br>
`cluster_ueval([<object>/]<attribute>/<role>[, <arg0>[, ... , <arg31>]])`

[u()], [u2()], [ulocal()], [u2local()], [udefault()], [uldefault()], [u2default()], [u2ldefault()] and [ueval()] on the cluster. Without an object, the cluster is the object calling the function.

```sharp
> think cluster_u(#456/FN,a,b)
Got a and b
```

## cluster_hasattr()

`cluster_hasattr(<object>[/<attribute>][, <attribute>])`

[hasattr()] on the cluster.

```sharp
> think [cluster_hasattr(#123/VA)] [cluster_hasattr(#123,NOPE)]
1 0
```

## cluster_lattr()

`cluster_lattr(<object>[/<pattern>][, <target>[, <type>[, <flags>[, <regexp>[, <tree>]]]]])`<br>
`cluster_attrcnt(<object>[/<pattern>][, <key>])`<br>
`cluster_vattrcnt(<object>)`

`cluster_lattr()` is [lattr()] on the cluster, and `cluster_vattrcnt()` is [vattrcnt()]. `cluster_attrcnt()` counts the cluster's attributes, or those matching *\<pattern\>*: a wildcard pattern, or a regular expression when it starts with `^`. *\<key\>* chooses how RhostMUSH treats a damaged attribute; SharpMUSH has none, so it changes nothing.

```sharp
> think [cluster_lattr(#123/V*)] / [cluster_attrcnt(#123)] [cluster_attrcnt(#123/V*)]
VA VB VC / 4 3
```

## cluster_flags()

`cluster_flags(<object>[/<attribute>])`<br>
`cluster_hasflag(<object>[/<attribute>], <flag>)`

[flags()] and [hasflag()] on the cluster.

```sharp
> think cluster_hasflag(#123,wizard)
0
```

## cluster_grep()

`cluster_grep(<object>, <attributes>, <pattern>[, <type>[, <key>]])`<br>
`cluster_regrep(<object>, <attributes>, <regexp>[, <key>])`<br>
`cluster_regrepi(<object>, <attributes>, <regexp>[, <key>])`

`cluster_grep()` lists the attributes, of those *\<attributes\>* names, whose value matches *\<pattern\>*, ignoring case. A pattern with no `*` or `?` matches anywhere in the value; with *\<key\>* 1 it must be a whole word of the value. *\<type\>* 1 writes the cluster's dbref in brackets before the names, and type 2 writes each as `#<dbref>/<attribute>`.

`cluster_regrep()` and `cluster_regrepi()` are [regrep()] and [regrepi()] on the cluster. With *\<key\>* 1, *\<attributes\>* is a regular expression too.

```sharp
> think [cluster_grep(#123,v*,is)] / [cluster_grep(#123,v*,is,,1)] / [cluster_grep(#123,v*,whisooo,2)]
VA VB VC / VA VB / #123/VC
```

## cluster_set()

`cluster_set(<object>[/<attribute>], <flag or attribute:value>)`<br>
`cluster_wipe(<object>[/<pattern>][, <regexp>[, <owner>[, <preserve>]]])`

[set()] and [wipe()] on the cluster. `cluster_wipe()` takes a wildcard pattern only: given 1 for *\<regexp\>*, *\<owner\>* or *\<preserve\>*, it wipes nothing and gives `#-1 CLUSTER_WIPE TAKES A WILDCARD PATTERN ONLY`. Both are side effects.

```sharp
> think [cluster_set(#456,TMP:one)][get(#123/TMP)]
one
```

## cluster_stats()

`cluster_stats(<object>, <type>[, <attribute>])`

Facts about a cluster you control. Type 0 gives 1. Type 1 lists the numbers the cluster answers to, its own first. Type 4 gives the cluster's dbref when it holds *\<attribute\>* with a value, and nothing when it does not. RhostMUSH's threshold and actions have nothing to do here, so types 2, 3 and 5 are empty and type 6 reports no delay.

```sharp
> think [cluster_stats(#123,1)] / [cluster_stats(#123,4,VB)]
#123 #456 / #123
```

## cluster_add()

`cluster_add(<cluster>, <object>)`

A cluster is one object here, so there is nothing to add an object to. `cluster_add()` changes nothing and gives `#-1 A CLUSTER IS ONE OBJECT HERE`.

::: seealso
- [@cluster]
- [compatibility names]
:::
