# plus-config

`+config` — one command for the settings the game's softcode systems keep for themselves. A system
declares each setting on its own object, with the type and limits it accepts, and registers the
object here. `+config` lists the settings, checks every new value against its declaration and writes
it back to the system's object.

The engine's own options belong to `@config`, and a package sets those through `settings:` in its
manifest. `+config` never writes engine options.

## Commands

| Command | Who | What |
|---|---|---|
| `+config` | all | Every system that has settings |
| `+config <system>` | all | Its settings: value, what each accepts, what each does; `*` marks a changed value |
| `+config <system>/<setting>` | all | One setting, with its default and who may change it |
| `+config <system>/<setting>=<value>` | config.admin | Change it |
| `+config/add <system>/<setting>=<items>` | config.admin | Add words to a list |
| `+config/remove <system>/<setting>=<items>` | config.admin | Take words out of a list |
| `+config/reset <system>/<setting>` | config.admin | Back to the default |
| `+config/register <system>=<object>` | config.admin | Register hand-made code |
| `+config/unregister <system>` | config.admin | Drop a hand registration |

`config.admin` is the permission `@config` needs. A setting can name another in its `PERMISSION`
leaf; finer control is `@lock/command` or `@command/restrict`, not more permissions.

## Giving a package settings

Declare each setting on the object whose code reads it, and attach one leaf to the registry:

```yaml
depends:
  - package: plus-config
    version: ">=1.0"
    source:
      repo: https://github.com/SharpMUSH/SharpMUSH
      path: examples/packages/plus-config/
      branch: main

objects:
  - ref: my_object
    type: thing
    name: My System
    attributes:
      DATA`CONFIG:
        value: |-
          What these settings change, in one line.
        flags: [no_command]
      DATA`CONFIG`HOURS: |-
        14400
      DATA`CONFIG`HOURS`TYPE: |-
        duration
      DATA`CONFIG`HOURS`DEFAULT: |-
        14400
      DATA`CONFIG`HOURS`MIN: |-
        600
      DATA`CONFIG`LABEL: |-
        My System
      DATA`CONFIG`HOURS`DESCRIPTION: |-
        How long a status lasts.
      DATA`CONFIG`HOURS`LABEL: |-
        Status length
      DATA`CONFIG`HOURS`EXAMPLE: |-
        2h

  - ref: my_config_registration
    target: "{{plus-config/registry}}"
    attributes:
      SYSTEM`MY-SYSTEM: |-
        {{my_object}}
```

The code reads the setting where it lives: `v(DATA`CONFIG`HOURS)` on that object, or
`get({{my_object}}/DATA`CONFIG`HOURS)` from another. It needs nothing from `plus-config` to read, so
a package works with its defaults even where `+config` is not installed.

`<setting>` and `<system>` are letters, digits, `-` and `_`; `+config` matches them in any case.
`DATA`CONFIG`LABEL` is the system's display name (`Who and Where`), so no setting is called `label`.
`no_command` on `DATA`CONFIG` reaches every leaf below it.

### Leaves

| Leaf | Meaning |
|---|---|
| (the setting itself) | The current value, in stored form |
| `TYPE` | Required. One of the types below |
| `DEFAULT` | What `/reset` writes, in stored form |
| `DESCRIPTION` | One line: what the setting changes |
| `LABEL` | Its display name, such as `Idle after`, shown beside the description and at the top of its screen |
| `EXAMPLE` | A value to show as an example, in typed form: `45m` shows `+config <system>/<setting>=45m` |
| `MIN`, `MAX` | Bounds: a number's value, a duration in seconds, a text's length, a list's size |
| `CHOICES` | The words a `choice` takes, or the words a `list` may hold |
| `PATTERN` | A regular expression a `text`, or each word of a `list`, must match |
| `DELIMITER` | For `list`: what separates the items, such as `\|` for names with spaces in them; a space when unset |
| `OBJECTTYPE` | For `object`: any of `ROOM PLAYER THING EXIT` |
| `CHECK` | The name of an attribute on the same object: `%0` the value as stored, `%1` the setting; it returns nothing when the value is fine, or the reason it is not |
| `PERMISSION` | The permission that changes this setting, instead of `config.admin` |

### Types

| Type | Accepts | Stored |
|---|---|---|
| `text` | anything | as typed |
| `integer` | a whole number | the number |
| `number` | any number | the number |
| `boolean` | yes no on off true false 1 0 | `1` or `0` |
| `duration` | `4h`, `30m`, `1d 12h`, `45` | seconds |
| `choice` | one word of `CHOICES`, any case | as `CHOICES` writes it |
| `list` | items separated by `DELIMITER` (spaces by default) | the same, each once, in the order given |
| `pattern` | a regular expression | as typed |
| `object` | a name or dbref the setter can find | objid |
| `timezone` | an IANA zone such as `Europe/London` | as typed |

Anything a type cannot say goes in a `CHECK`. A table whose entries carry several fields (statuses
with their hours, pose types) keeps its own commands; `+config` is for single values and lists.

### Reacting to a change

After a value changes, `+config` triggers the system object's `ACONFIG`, if it has one, with
`%<setting>`, `%<value>` (stored form), `%<previous>` and `%<by>` (the setter's objid).

### Upgrades

The manifest declares the value and the `DEFAULT` with the same text. The package manager keeps a
value an administrator changed when the package is upgraded, and an untouched value follows a new
default. When an upgrade changes the default of a value the administrator also changed, the review
screen asks which to keep.

## Hand-made code

Give the object the same `DATA`CONFIG` tree, then `+config/register <system>=<object>`. The
registry stores the object's objid. `+config/unregister <system>` drops it and leaves the settings on
the object. A package's registration cannot be dropped this way: it leaves when the package is
uninstalled.

## Why the registry is WIZARD

It reads and writes the `DATA`CONFIG` tree on objects it does not own. It writes only to a
registered object, only a setting with a `TYPE`, and only a value that passed the checks. Values are
stored as data and never evaluated, so the system's code reads them with `v()` or `get()`.
