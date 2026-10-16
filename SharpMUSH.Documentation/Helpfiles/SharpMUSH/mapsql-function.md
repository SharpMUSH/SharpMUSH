<!-- help-article
{
  "corpus": "help",
  "id": "mapsql-function",
  "lookup": "mapsql()",
  "aliases": [],
  "sections": [
    {"id": "rows", "heading": "Rows and field names", "lookup": "mapsql rows"},
    {"id": "prepared", "heading": "Prepared statements", "lookup": "mapsql prepared statements"}
  ]
}
-->
# mapsql()
`mapsql([<object>/]<attribute>, <query>[, <osep>[, <dofieldnames>[, <param1>[, <param2>[, ...]]]]])`

  Performs an SQL query if the MUSH is configured to connect to an SQL database server. This function requires a WIZARD flag or the Sql_Ok power.

  `<query>` is evaluated, so it's useful to either read it from another attribute with u() or use lit() to protect commas. If you will be interpolating user-provided values into the query, be careful to escape them with sqlescape().

- [mapsql rows]: what the attribute receives for each row
- [mapsql prepared statements]: parameterized queries and an example

## Rows and field names

  Each row of the result is passed to `<attribute>`, with the first nine columns available as %1-%9, and twenty more available via v(10) - v(29). %0 is set to the row number, which will start with 1. The fields will also be available as named arguments, with r(`<field name>`, args) returning the appropriate value.

  If `<dofieldnames>` evaluates to a true boolean, then the first call will be with row number (%0) set to 0, and %1-%9 and v(10) - v(29) will be set to the field names.

## Prepared statements

  **Prepared Statements (Parameterized Queries):** If more than 4 arguments are provided, mapsql() automatically uses prepared statements. The 5th argument onwards are treated as parameters that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe.

  Example using prepared statements:
```sharp
  > &DisplayRow me=User %1 has email %2
  > think mapsql(me/DisplayRow,lit(SELECT name\, email FROM users WHERE status = ?),%r,0,active)
  ```

  See [SQL Examples] for examples.


::: seealso
- [anonymous attributes]
- [SQLESCAPE()]
- [SQL()]
- [@sql]
- [@mapsql]
:::
