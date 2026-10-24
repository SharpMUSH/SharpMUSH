<!-- help-article
{
  "corpus": "help",
  "id": "bit-locks",
  "lookup": "@lock-bit",
  "aliases": ["@lock-flag", "@lock-type", "@lock-power"],
  "sections": [
    {"id": "roles", "heading": "Role, permission and channel locks", "lookup": "@lock-bit roles", "aliases": ["@lock-role", "@lock-perm", "@lock-channel"]}
  ]
}
-->
# Bit Locks

You can test for set flags, powers, roles, permissions, or object types in a lock directly, without using an evaluation lock, with these formats:

`@lock <object>=flag^<flag>`<br>
`@lock <object>=power^<power>`<br>
`@lock <object>=type^<type>`

`flag^<flag>` passes when the object trying the lock has the flag set, as hasflag() reports; `power^<power>` when it has the power, as haspower() reports; and `type^<type>` when it is of that type, as hastype() reports.

For example:

```sharp
> @lock/use Admin Commands=flag^wizard|flag^royalty
```

- [@lock-bit roles]: testing for a role, a permission or channel membership

## Role, permission and channel locks

To test for a role or a permission (see [roles]):

`@lock <object>=role^<role>`<br>
`@lock <object>=perm^<permission>`

`role^<role>` passes when the object trying the lock holds the role, its own or its account's, as hasrole() reports. `perm^<permission>` passes when it holds the permission, as permission() reports.

```sharp
> @lock/use Staff Board=role^moderator|perm^players.moderate
```

You can also test for channel membership with:

`@lock <object>=channel^<channel>`

`channel^<channel>` passes when the object trying the lock is on that channel.
