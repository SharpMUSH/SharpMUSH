# Contact
# Code
SharpMUSH is developed by a team of developers whose names are listed in [credits]. Suggestions, comments, and bug reports are welcome.

The main SharpMUSH web page is at http://sharpmush.com

Report bugs and make suggestions at https://github.com/SharpMUSH/SharpMUSH/issues

The SharpMUSH Discord is at https://discord.com/invite/jYErRbqaC9

For information about downloading SharpMUSH, see [download].<br>
For what SharpMUSH adds to PennMUSH, see [sharpmush features].

# Download
The latest version of this MUSH code is available from https://github.com/SharpMUSH/SharpMUSH/releases. 

# Exception
# #-1 exception
When an internal error escapes a command, SharpMUSH does **not** stay silent. The
command returns, and you are notified with:

```sharp
  #-1 EXCEPTION: {"id":"7ac09f1a5b6f","command":"@switch","type":"KeyNotFoundException"}
```

The payload is a single-line JSON object. Everyone sees three fields:

* `id`: a correlation id. The full error, including its stack trace, is in the
  server log under this same id. Quote it when you report the bug; a wizard can
  find the entry from it without you having to reproduce anything.
* `command`: the command word that failed.
* `type`: the class of error.

A wizard or royalty additionally sees `message` (the error text) and `inner` (the
chain of underlying errors). Those are withheld from everyone else because an
error message can quote a file path or a database connection string. Stack traces
are never sent to anyone in-game; they stay in the log, reachable by `id`.

This is a real error return, so softcode can test for it: `#-1 EXCEPTION:` is
always the prefix, and the rest is always valid JSON.

::: seealso
- [pennmush compatibility]
:::

# Internationalization
# I18n
# Locale
# Translation
SharpMUSH supports Unicode: server strings use UTF-16 internally, and network
text supports UTF-8. The `accent()` and `stripaccents()` functions and the
NOACCENTS flag provide compatibility with older clients. See [unicode].

Server messages use the existing locale-based resource system. The portal has
its own translated resources and language picker. Help translations use
locale-specific corpus directories, falling back to the neutral help source
when a translated lookup is unavailable. Command and function aliases can
provide translated names without duplicating their implementation.

Date/time formatting and string collation use the applicable locale facilities.
Choose a locale through the existing player and portal preferences; do not assume
that setting a process environment variable translates every presentation.
