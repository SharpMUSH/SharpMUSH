<!-- help-article
{
  "corpus": "help",
  "id": "mapsql-command",
  "lookup": "@mapsql",
  "aliases": [],
  "sections": [
    {"id": "rows", "heading": "Rows and column names", "lookup": "@mapsql rows", "aliases": ["@mapsql/colnames"]},
    {"id": "notify-spoof", "heading": "Notify and spoof", "lookup": "@mapsql notify and spoof", "aliases": ["@mapsql/notify", "@mapsql/spoof"]},
    {"id": "prepare", "heading": "Prepared statements", "lookup": "@mapsql prepare", "aliases": ["@mapsql/prepare"]},
    {"id": "examples", "heading": "Examples", "lookup": "@mapsql examples"}
  ]
}
-->
# @mapsql

`@mapsql[/notify][/colnames][/spoof][/prepare] <obj>/<attr>=<query>[, <param1>[, <param2>[, ...]]]`

This command issues an SQL query if the MUSH supports SQL and can connect to an SQL server. You must be WIZARD or have the Sql_Ok power to use @mapsql.

The server connects with the sql_platform, sql_host, sql_database and sql_username options and the password stored as the `sql.password` secret (see [@secret]).

- [@mapsql rows]: what each queued row receives, and the column-name row
- [@mapsql notify and spoof]: a completion notify, and keeping the enactor
- [@mapsql prepare]: placeholders and parameters against SQL injection
- [@mapsql examples]: describing a table, named fields, and a prepared query

## Rows and column names

`@mapsql/colnames <obj>/<attr>=<query>`

For each row returned by the query, the action list in `<obj>/<attr>` is queued, with row number passed as %0 and the columns passed as %1-%9 and v(10) to v(29). Row numbers start at 1. The MUSH will also set named arguments, with arg names matching the SQL field names. These are accessible as `r(<name>, arg)`.

The `/colnames` switch causes @mapsql to first queue the obj/attr with row number (%0) set to 0 and args %1 to v(29) being the column names.

## Notify and spoof

`@mapsql/notify <obj>/<attr>=<query>`<br>
`@mapsql/spoof <obj>/<attr>=<query>`

The `/notify` switch queues `@notify me` after the admitted row callbacks. This runs as the object executing `@mapsql`. One queue slot is reserved for completion before the query starts; if that reservation fails, the query does not run. Completion still runs for an empty result or partial row admission, but a query error releases its reservation without notifying.

By default, the object using @mapsql will be the enactor (%#) for the triggered attribute. However, if you control `<object>`, the `/spoof` switch can be used to preserve the current enactor.

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

Prepared statement example:
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
