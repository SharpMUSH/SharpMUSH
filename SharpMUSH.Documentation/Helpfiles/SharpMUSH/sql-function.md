<!-- help-article
{
  "corpus": "help",
  "id": "sql-function",
  "lookup": "sql()",
  "aliases": [],
  "sections": [
    {"id": "prepared", "heading": "Prepared statements", "lookup": "sql prepared statements"},
    {"id": "connections", "heading": "Connections and values", "lookup": "sql connections"}
  ]
}
-->
# sql()
`sql(<query>[, <row separator>[, <field separator>[, <register>[, <param1>[, <param2>[, ...]]]]]])`

Performs an SQL query if the MUSH is configured to connect to an SQL database server. This function requires a WIZARD flag or the Sql_Ok power. A query can change the database it reaches, so the function is refused when side-effect functions are turned off.

By default, SELECT queries will return their data space-separated. Usually, it's more useful to specify a character to delimit rows returned (and sometimes another character to delimit the fields/columns returned, if they may contain spaces).

`<query>` is evaluated, so it's useful to either read it from another attribute with u() or use lit() to protect commas. If you will be interpolating user-provided values into the query, be careful to escape them with sqlescape().

A query that doesn't return any rows, such as an UPDATE or SELECT that has no matches will return a null string.

If `<register>` is specified, there is a side-effect: the number of rows the query returned is stored into the specified q-register. A query that returns no rows, such as an UPDATE or INSERT, stores 0.

- [sql prepared statements]: parameterized queries and an example
- [sql connections]: what lasts between queries, and how values come back

## Prepared statements

With more than 4 arguments, sql() uses a prepared statement: the 5th argument onwards are parameters that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe.

```sharp
> think sql(lit(SELECT name FROM users WHERE id = ?),%r,%b,,123)
```

See [SQL Examples] for more examples.

## Connections and values

Each sql() call borrows a connection from a pool and gives it back when the query ends, so the next call may get another connection or the same one. Do not count on session state from one call to the next: LAST_INSERT_ID(), a temporary table or a `@variable` set in one sql() may or may not be there in the next. Put the statements that share state in one call.

MySQL and MariaDB session variables work inside a call:

```sharp
> think sql(lit(SELECT @rank := 5),|,~)
```
Output: `5`

A binary column (BLOB, BINARY) comes back as the text its bytes spell, read as UTF-8 where they are valid UTF-8 and as Latin-1 otherwise. A NULL comes back empty.

The command that runs a query waits for its answer, and so does the rest of the queue. When its queue entry's time runs out, the query is cancelled where the database client supports it. SQLite finishes a statement it has started.

::: seealso
- [SQLESCAPE()]
- [MAPSQL()]
- [@sql]
- [setq()]
- [R()]
- [@mapsql]
- [cache()]
:::
