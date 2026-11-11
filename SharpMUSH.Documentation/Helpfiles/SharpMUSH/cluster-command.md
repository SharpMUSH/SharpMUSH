<!-- help-article
{
  "corpus": "help",
  "id": "cluster-command",
  "lookup": "@cluster",
  "aliases": [
    "@rhost_cluster"
  ],
  "sections": []
}
-->
# @cluster

`@cluster/list <cluster>`<br>
`@cluster/set <cluster>=<attribute>:<value>`<br>
`@cluster/set <cluster>/<attribute>=<attribute flag>`<br>
`@cluster/edit <cluster>/<pattern>=<from>,<to>`<br>
`@cluster/grep[/regexp] <cluster>=<attributes>,<pattern>`<br>
`@cluster/wipe <cluster>[/<pattern>]`<br>
`@cluster/trigger <cluster>/<attribute>[=<arguments>]`

RhostMUSH's command for its clusters. A RhostMUSH import merges each cluster into one object, which answers to the numbers of the objects merged into it (see [rhostmush clusters]). @cluster works on that object.

/list shows the cluster's name, the numbers it answers to and how many attributes it has. /set, /edit, /wipe and /trigger are [@set], [@edit], [@wipe] and [@trigger] on the cluster, and /grep is [@grep]: a pattern with `*` or `?` is matched as a wildcard, /regexp as a regular expression, and any other matches anywhere in a value. The text after the cluster's name goes on as typed, so each command evaluates it as it always does.

The switches that build or tune a cluster (/new, /add, /del, /clear, /cut, /repair, /threshold, /action, /func and /reaction) have nothing to do, and say so. /wipe takes no /regexp, /owner or /preserve.

@cluster is reached under the RhostMUSH dialect (see [compatibility config]); its own name everywhere is @rhost_cluster.

```sharp
> @cluster/set #456=SCORE:[add(2,3)]
> think get(#123/SCORE)
5
```

::: seealso
- [rhostmush clusters]
:::
