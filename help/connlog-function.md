<!-- help-article
{
  "corpus": "help",
  "id": "connlog-function",
  "lookup": "connlog()",
  "aliases": [
    "connlog"
  ],
  "sections": [
    {
      "id": "search-filters",
      "heading": "Search filters",
      "lookup": "connlog search filters"
    },
    {
      "id": "connection-search-examples",
      "heading": "Connection search examples",
      "lookup": "connlog connection search examples"
    }
  ],
  "redirects": {
    "CONNLOG2": "connlog search filters",
    "CONNLOG3": "connlog connection search examples"
  }
}
-->
# connlog()

`CONNLOG(all|[not] logged in|<name>, <spec>, <spec>...[, <osep>])`

If connection tracking is enabled, this Wizard-only returns a list of connections that match the given `<spec>`. The format of the list elements is '`<dbref>` `<unique-id>`', with elements seperated by `<osep>` (defaulting to |). `<unique-id>` is an identifier that can be used to get more information from the connection with connrecord().

If the first argument is 'all', all connections are returned. If it's 'logged in', all connections that are logged in to players are returned. 'not logged in' shows connections that never logged in. Otherwise, only connections for the given player are returned. If a connection that never logged in is returned, the dbref is #-1 for that record.

This function must be enabled (by the use_connlog @config option); if disabled, it returns #-1.

## Search filters

`<spec>` is one or more of the following:

Time-based constraints:
    * between, `<startsecs>`, `<endsecs>` - connections that existed during the given time frame.
    * at, `<secs>` - connections that existed at the given time.
    * before, `<secs>` - connections that existed before the given time.
    * after, `<secs>` - connections that existed after the given time.
Only one time-based constraints can be used in a query. All times are the number of seconds since the epoch, as returned by secs().

Source-based constraits:
    * ip, `<pattern>` - connections from IP addresses that match the wildcard `<pattern>`.
    * hostname, `<pattern>` - connections from hostnames that match the wildcard `<pattern>`.

Others:
    * count - if given, instead of returning a list of connections, returns the total number of matching connections.

::: seealso
- [ADDRLOG()]
- [CONNRECORD()]
:::

## Connection search examples

Examples:
```sharp
think connlog(logged in, after, secscalc(now, -15 minutes))
shows all connections that were present during the last 15 minutes
```

```sharp
> think connlog(all, ip, 127.0.0.1)
shows all connections ever made from localhost.
```

```sharp
> think connlog(all, count, before, secs())
shows the total number of connections made since logging began.
```
