<!-- help-article
{
  "corpus": "help",
  "id": "mapsql-command",
  "lookup": "@mapsql",
  "aliases": [],
  "sections": [
    {"id": "rows", "heading": "Rows and column names", "lookup": "@mapsql rows", "aliases": ["@mapsql/colnames"]},
    {"id": "notify-spoof", "heading": "Notify and spoof", "lookup": "@mapsql notify and spoof", "aliases": ["@mapsql/notify", "@mapsql/spoof"]},
    {"id": "then", "heading": "Running something afterwards", "lookup": "@mapsql then", "aliases": ["@mapsql/then"]},
    {"id": "prepare", "heading": "Prepared statements", "lookup": "@mapsql prepare", "aliases": ["@mapsql/prepare"]},
    {"id": "examples", "heading": "Examples", "lookup": "@mapsql examples"}
  ]
}
-->
# @mapsql

`@mapsql[/notify][/colnames][/spoof][/prepare] <obj>/<attr>=<query>[, <param1>[, <param2>[, ...]]]`<br>
`@mapsql/then <obj>/<attr>=<query>,<afterwards>`

This command issues an SQL query if the MUSH supports SQL and can connect to an SQL server. You must be WIZARD or have the Sql_Ok power to use @mapsql.

The server connects with the sql_platform, sql_host, sql_database and sql_username options and the password stored as the `sql.password` secret (see [@secret]).

The query runs while the game goes on, and each row is queued as it arrives, so work queued after @mapsql can run before its rows; use `/then` to run something after the last row. The query has 60 seconds, and @halt on the object using @mapsql stops one still running. With the `mapsql_sync` option on (a PennMUSH database import turns it on), the query runs inside the command, as in PennMUSH: the game waits for it, and every row is queued before anything queued after @mapsql.

- [@mapsql rows]: what each queued row receives, and the column-name row
- [@mapsql notify and spoof]: a completion notify, and keeping the enactor
- [@mapsql then]: an action list run after the last row
- [@mapsql prepare]: placeholders and parameters against SQL injection
- [@mapsql examples]: describing a table, named fields, and a prepared query

## Rows and column names

`@mapsql/colnames <obj>/<attr>=<query>`

For each row returned by the query, the action list in `<obj>/<attr>` is queued, with row number passed as %0 and the columns passed as %1-%9 and v(10) to v(29). Row numbers start at 1. The MUSH will also set named arguments, with arg names matching the SQL field names. These are accessible as `r(<name>, arg)`.

The `/colnames` switch causes @mapsql to first queue the obj/attr with row number (%0) set to 0 and args %1 to v(29) being the column names.

## Notify and spoof

`@mapsql/notify <obj>/<attr>=<query>`<br>
`@mapsql/spoof <obj>/<attr>=<query>`

The `/notify` switch queues `@notify me` after the rows, run as the object using @mapsql, so code waiting on its semaphore knows the query is done. It runs when the query returns no rows, and when a full queue took only some of them, but not when the query fails. If the queue has no room left for the notify, the query does not run.

By default, the object using @mapsql will be the enactor (%#) for the triggered attribute. However, if you control `<object>`, the `/spoof` switch can be used to preserve the current enactor.

## Running something afterwards

`@mapsql/then <obj>/<attr>=<query>,<afterwards>`

With `/then`, the action list after the last comma is queued after the last row, run as the object using @mapsql, with the number of rows queued in %0. It runs when the query returns no rows, but not when the query fails. With `/prepare`, the parameters come before it. Put braces around a query or an action list that has a comma of its own.

```
@mapsql/then me/ROW={SELECT name, score FROM scores},{@pemit me=%0 scores.}
```

## Prepared statements

`@mapsql/prepare <obj>/<attr>=<query>[, <param1>[, <param2>[, ...]]]`

The `/prepare` switch enables prepared statement mode. When used, additional comma-separated parameters after the query are treated as values that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe. When using `/prepare` with queries containing commas, store the query in an attribute and use v() to retrieve it, or escape commas with backslash.

## Examples

```sharp
> &desctable me=think align(30 20 4 10 10,%0,%1,%2,%3,%4)
> @mapsql me/desctable=DESCRIBE table_name
```

```sharp
> &showresult me=@pemit %#=%0. [r(name, arg)] ([r(age, arg)])
> @mapsql me/showresult=SELECT `name`, `age` FROM `people`
```

A prepared statement:

```sharp
> &showresult me=@pemit %#=%0. %1 (%2)
> &QUERY me=SELECT `name`, `age` FROM `people` WHERE status = ?
> @mapsql/prepare me/showresult=v(QUERY),active
```

::: seealso
- [@sql]
- [SQL()]
- [SQLESCAPE()]
- [MAPSQL()]
:::
