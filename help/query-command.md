<!-- help-article
{
  "corpus": "help",
  "id": "query-command",
  "lookup": "@query",
  "aliases": [
    "@dialect_query"
  ],
  "sections": []
}
-->
# @query

`@query/sql <object>/<attribute>=<database>/<query>`

@query runs an SQL query and triggers the attribute once for each row, exactly as [@mapsql] does: the row number is in `%0` and the columns in `%1` onward. SharpMUSH has one SQL connection, so the database name before the slash is not used. The query is evaluated before it runs, and @query needs the same permission as @mapsql.

@query is TinyMUX's command, reached under the `mux` dialect (see [compatibility config]); its own name everywhere is @dialect_query.

```sharp
> &ROW Logger=@pemit %#=Row %0: %1
> @query/sql Logger/ROW=game/SELECT name FROM scores
Row 1: Alice
Row 2: Bob
```

::: seealso
- [@mapsql]
- [@sql]
:::
