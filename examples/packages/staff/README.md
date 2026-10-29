# staff

`+staff` and `+stafflist`: the game's staff, each with the duties they hold, their staff role, whether they
are on duty and how long they have been idle.

```
> +staff
+==================================< Staff >=================================+
| Name  Duty                                 Role     Status            Idle |
| -------------------------------------------------------------------------- |
| John  Head Director                        Wizard   [On duty] * LFRP   10s |
| Ann   Plot Staff, Theme                    Royalty  Off duty           11s |
| Jim   Builder                                       Offline                |
| Kit                                        Helper   Offline                |
+============================================================================+
| 4 staff, 2 connected                                                       |
| Open: Storyteller                                                          |
+============================================================================+
```

- **Listed by role.** Every character holding one of the roles in the `roles` setting is listed, with no
  list to keep: give someone the `royalty` role and they are on `+staff`. The roles are found with
  `lsearch(all, type, player, elock, role^<role>|...)`, so a role held through an account lists every
  character on that account. God (#1) and the package manager hold the wizard role to run the game, so
  they are left out unless they hold a duty.
- **Duties are a table.** A duty is an entry of the `duties` table on the Staff Store, with its title, the
  character holding it (an objid, so a rename keeps it) and an order. Someone holding a duty is listed
  even without a staff role, and a duty nobody holds is named in the footer as open.
- **Order.** A character is listed at their lowest duty's order, then by the place of their role in the
  `roles` setting, then by name.
- **On duty.** A listed staff member types `+staff/on` and `+staff/off`; ``ONDUTY`<key>`` on the Staff
  Store marks them on duty until they go off. Status is `On duty` or `Off duty` while they are connected.
- **Who is on.** Status and Idle are read only for characters on the who list as a mortal sees it
  (`mwho()`), so a hidden staff member shows Offline, on duty or not.

## Extending the Status column

Another package changes the Status column with a filter on the Staff List: an attribute named
``FILTER`STAFF`STATUS`<name>``. Filters run in name order with `chain()`; each gets the status so far as
`%0`, the character as `%1` and `1` as `%2` when they are connected, and returns the status to show.
[`staff-status`](../staff-status/) is one: it joins this package and `status`, adding each connected staff
member's `+status` tag, `On duty · LFRP`.

## Settings

The `staff-config` bridge registers these with [`+config`](../plus-config/) (`+config staff`). Without
`+config`, the defaults stand and the table is empty.

| Setting | Type | Default | What it changes |
|---|---|---|---|
| `roles` | list | `immortal wizard moderator councilor royalty architect guildmaster helper` | The roles that put a character on `+staff`, highest first |
| `duties` | table | none | The duties: `title` (shown), `holder` (a player), `order` (0 to 999, 50 when added) |

```
> +config/add staff/duties/director=Head Director
> +config staff/duties/director/holder=*John
> +config staff/duties/director/order=10
```

## Objects

- **Staff List**, in the master room, `WIZARD` so its `lsearch()` sees every character: the commands, the
  settings and the screen.
- **Staff Store**, inside it, `NO_COMMAND`: the duties, as ``DUTY`<NAME>`` with `HOLDER` and `ORDER` beneath,
  and who is on duty, as ``ONDUTY`<key>``.
- **Staff List Help**, inside it: the `+help staff` topics. The `staff-help` bridge registers them with the
  `+help` librarian.
