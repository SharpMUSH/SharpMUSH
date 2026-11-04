# app-builder

Serves the forms, sheets and widgets made with the portal's **Application builder**
(`/build/apps`). A built application needs no route code of its own: the builder writes its
document and its maps as attributes on an object its author controls, and this package's three
routes read and save them.

## What the builder writes

On the object the author picks, with `<SLUG>` the application's name in capitals:

| Attribute | Holds |
|---|---|
| `` DATA`APP`<SLUG> `` | The Portal Schema Document |
| `` DATA`APP`<SLUG>`SOURCES `` | JSON object: field key to the attribute it is read from, on the viewed character |
| `` DATA`APP`<SLUG>`SAVE `` | JSON object: field key to the attribute it is written to, on the sender (forms) |
| `` DATA`APP`<SLUG>`REQUIRED `` | The keys a sent form must fill, space-separated (forms) |

## Commands

| Command | Who | What |
|---|---|---|
| `+builder` | all | The registered applications and the object holding each |
| `+builder/register <slug>=<object>` | `applications.admin` | Serve the application held on `<object>`; you must control it |
| `+builder/unregister <slug>` | `applications.admin` | Stop serving it; its attributes stay |

## Routes

| Route | Answers |
|---|---|
| `GET http/built/schema?app=<slug>` | The stored document |
| `GET http/built/data?app=<slug>[&objid=<objid>]` | `{fields}`: each SOURCES value read as the viewer, from `objid` or the viewer |
| `POST http/built/act?app=<slug>` | Checks REQUIRED, then writes each SAVE field on the sender, as the sender |

A route answers only for a registered slug. Reads and writes run as the viewer through
`objeval()`, so a viewer sees only what `get()` would show them and changes only what `&` would
let them change. A viewer the package's object does not control (God) is read nothing and can
save nothing.

`app-builder-help` puts the `+help builder` topics into `+help`.
