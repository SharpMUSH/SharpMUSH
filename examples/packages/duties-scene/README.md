# Duties in scenes

Ties a [Duties](../duties/) party to a scene, so the scene's log holds the duty as it went.

- `+duty/start/scene <duty>` gathers a party and makes a scene for it, owned by the leader, in their room, titled after the duty with its description as the pitch.
- `+duty/scene <number>` ties the leader's party to a scene already running; `+duty/scene` says which scene the party's duty goes into.

Everyone in the party joins the scene and is focused on it, and so does each player who joins the party later. Setting out, each step's text, each roll, each outcome and the end go into the scene as narration under the duty's title. A paused, finished or cancelled scene records nothing.

## Needs

- `duties` 1.2 or later, which tells its extensions what happens to a party.
- `scene` and the Scene plugin.

## How it works

The package attaches `EXTENSION`SCENE` to the Duties object. Duties runs `DO`SCENE` and `FLAG`START`SCENE` from it for `+duty/scene` and `+duty/start/scene`, and triggers its `ON`DUTIES`JOIN`, `ON`DUTIES`LINE` and `ON`DUTIES`FINISH` as the party plays. `+help duty extensions` describes the hooks.
