<!-- help-article
{
  "corpus": "help",
  "id": "sql-command",
  "lookup": "@sql",
  "aliases": [],
  "sections": [
    {"id": "prepare", "heading": "Prepared statements", "lookup": "@sql prepare", "aliases": ["@sql/prepare"]},
    {"id": "examples", "heading": "Examples", "lookup": "@sql examples"}
  ]
}
-->
# @sql

`@sql[/prepare] <query>[, <param1>[, <param2>[, ...]]]`

This command issues an SQL query if the MUSH supports SQL and can connect to an SQL server. You must be WIZARD or have the Sql_Ok power to use @sql.

The server connects with the sql_platform, sql_host, sql_database and sql_username options and the password stored as the `sql.password` secret (see [@secret]).

Output: the query's result, as it shows it.

Generally, the sql() function is more useful for coding, as it delimits its return values, but @sql is handy for INSERT-type queries and quick checks. If you pass arbitrary data to @sql, be sure you call sqlescape() on it; see the example in help sql().

- [@sql prepare]: placeholders and parameters against SQL injection
- [@sql examples]: listing tables, and prepared inserts and updates

## Prepared statements

`@sql/prepare <query>[, <param1>[, <param2>[, ...]]]`

The `/prepare` switch enables prepared statement mode. When used, additional comma-separated parameters after the query are treated as values that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe. When using `/prepare` with queries containing commas, store the query in an attribute and use v() to retrieve it, or escape commas with backslash.

## Examples

```sharp
> @sql SHOW TABLES
```

Prepared statement examples:
```sharp
> &QUERY me=INSERT INTO users (name, email) VALUES (?, ?)
> @sql/prepare v(QUERY),John Doe,john@example.com
```

```sharp
> &QUERY me=UPDATE users SET status = ? WHERE id = ?
> @sql/prepare v(QUERY),active,123
```

::: seealso
- [SQL()]
- [SQLESCAPE()]
- [MAPSQL()]
- [@mapsql]
:::
