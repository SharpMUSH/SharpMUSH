<!-- help-article
{
  "corpus": "help",
  "id": "sql-function",
  "lookup": "sql()",
  "aliases": [],
  "sections": [
    {"id": "prepared", "heading": "Prepared statements", "lookup": "sql prepared statements"}
  ]
}
-->
# sql()
`sql(<query>[, <row separator>[, <field separator>[, <register>[, <param1>[, <param2>[, ...]]]]]])`

Performs an SQL query if the MUSH is configured to connect to an SQL database server. This function requires a WIZARD flag or the Sql_Ok power.

By default, SELECT queries will return their data space-separated. Usually, it's more useful to specify a character to delimit rows returned (and sometimes another character to delimit the fields/columns returned, if they may contain spaces).

`<query>` is evaluated, so it's useful to either read it from another attribute with u() or use lit() to protect commas. If you will be interpolating user-provided values into the query, be careful to escape them with sqlescape().

A query that doesn't return any rows, such as an UPDATE or SELECT that has no matches will return a null string.

If `<register>` is specified, there is a side-effect: the number of rows the query returned is stored into the specified q-register. A query that returns no rows, such as an UPDATE or INSERT, stores 0.

- [sql prepared statements]: parameterized queries and an example

## Prepared statements

With more than 4 arguments, sql() uses a prepared statement: the 5th argument onwards are parameters that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe.

```sharp
> think sql(lit(SELECT name FROM users WHERE id = ?),%r,%b,,123)
```

See [SQL Examples] for more examples.

::: seealso
- [SQLESCAPE()]
- [MAPSQL()]
- [@sql]
- [setq()]
- [R()]
- [@mapsql]
:::
