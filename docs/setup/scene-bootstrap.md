# Scene System — the bundled `scene` package

The C# Scene System ships only **mechanism**: storage (`ISceneService`), the
wizard `@SCENE` command, the `scene…` softcode functions, the `SCENE_ROOM` flag,
the `game.scene.{id}` realtime broadcast, and the portal. It ships **no policy** —
nothing captures a pose, decides who may start a scene, formats a room emit, or
spins up a temp room. That is all **game policy**, and it is delivered as
softcode by the **bundled `scene` package**.

> **This is now a default package.** You do **not** hand-build a `#SCENELOGGER`
> object. The server ships `examples/packages/scene/package.yaml`, lists it in
> `BundledPackages.All`, and `DefaultPackagesBootstrapService` installs it at
> first boot like any other default package. The package creates and owns a
> single **WIZARD** thing named **"Scene Logger"** that carries all the softcode
> below. Admins **customize via the package manager** (edit the managed
> attributes on the Scene Logger object, or fork the package) rather than editing
> a hand-built object. The rest of this document is the **softcode the package
> ships**, annotated so you can see exactly what it does.

There is intentionally **no `SceneOptions` config category**: every knob is
policy, so it is set as `&DATA\`*` attributes on the Scene Logger object and read
by the verbs below. One place, not two.

> **Attribute organization** follows the project `FUN\`/CMD\`/DATA\`/INCLUDE\``
> tree convention (same as the common-functions and http-handler packages,
> e.g. `FUN\`HEADER`, `GET\`PROFILE\`SCHEMA`): `DATA\`*` for config knobs (reads),
> `FUN\`*` for pure reads/guards (called via `u()`), `CMD\`*` for player verbs
> and the capture `$`-commands (writes go through `@scene/*`), and `INCLUDE\`*`
> for shared command tails composed via `@include` (keeps individual attributes
> small). Leaf names are UPPERCASE.

> **Writes are commands, reads are functions.** Every **data-write** in the
> package uses an **`@scene/<switch>`** command (gated only by `FLAG^WIZARD`,
> which the WIZARD Scene Logger satisfies) — e.g. `@scene/addpose <id>=…`,
> `@scene/set <id>/<key>=…`, `@scene/member <id>/<role>=…`. This means the
> package works **without** enabling `function_side_effects`. The **read**
> surface (`scenewhere`, `scenefocus`, `scene`, `scenemember`, `sceneposes`, …)
> is plain `Regular` and always works as functions.

> Style note: examples follow SharpMUSH softcode conventions — bare `%0`/`%#`/
> `%q<…>` substitutions are never wrapped in `[ ]` (brackets are for function
> calls only), and `firstof()` is preferred over nested `if()` chains.

---

## 1. The Scene Logger object (shipped by the package)

The package's create-mode manifest declares a single owned thing:

```yaml
objects:
  - ref: logger
    type: thing
    name: Scene Logger
    flags: [WIZARD]
    attributes:
      ...
```

It **must be WIZARD** — it is the `@hook/override` target, it runs the
wizard-only `@SCENE`/`@HOOK` commands, and the package lifecycle attributes
(AINSTALL/STARTUP) run **as this object** (`%!` / `me` = the Scene Logger), so
self-execution of the wizard-locked `@hook` works.

### Policy attributes (replaces config)

The package ships `DATA\`CAPTURE` and `DATA\`DEFAULT_STATUS`; add the rest by
editing the managed attributes on the Scene Logger via the package manager:

```mush
&DATA`CAPTURE        Scene Logger=1
&DATA`DEFAULT_STATUS Scene Logger=new
&DATA`DEFAULT_PUBLIC Scene Logger=0
&DATA`MAX_RECENT     Scene Logger=50
&DATA`STATUSES       Scene Logger=new scheduled active paused finished
&DATA`TEMP_ZONE      Scene Logger=#0
&DATA`TEMP_GRACE_MIN Scene Logger=30
&DATA`TEMP_MAX       Scene Logger=3
&DATA`SHARE_OWNER    Scene Logger=1
```

---

## 2. Capture — one path, `@hook/override`

Players pose with the native `pose`/`say`/`semipose` commands. An
`@hook/override` reproduces the room emit **and** records the pose. This is the
**only** capture path:

* `@hook/after` cannot be used — AFTER/BEFORE hooks run with **empty args**, so
  they never see the pose text. Only **OVERRIDE** passes the command input to a
  `$`-command.
* `@EMIT` is **not** hooked — the override replaces only the personal pose verbs.

The package's `AINSTALL` (once) and `STARTUP` (every boot) (re-)establish the
three OVERRIDE hooks idempotently, in the standard PennMUSH form
`@hook/<type> <command> = <object>, <attribute>`:

```mush
@hook/override POSE     = %!, CMD`CAPTURE`POSE
@hook/override SAY      = %!, CMD`CAPTURE`SAY
@hook/override SEMIPOSE = %!, CMD`CAPTURE`SEMI
```

The three capture attributes are `$`-commands on the Scene Logger. Each (a)
reproduces the **built-in** room emit byte-for-byte, then delegates the shared
tail to `INCLUDE\`CAPTURE` via `@include %!/INCLUDE\`CAPTURE=<source>,<content>`.
The tail (b) `@assert`s a scene is active here **and** the poser is focused on
it, then (c) records the rendered line with `@scene/addpose` (note the
**explicit author** `%#`). Factoring the tail keeps each capture attribute small
(guide: "keep individual attributes smaller"). The `@assert` halts the command
list *after* the emit already fired, so a pose outside any scene behaves exactly
like the un-hooked built-in. **Writes are commands.**

Each capture hook is written as ONE physical line (commands separated by `;`),
and the assert+addpose tail is **inlined** into each hook rather than factored
into a shared `&INCLUDE\`CAPTURE` attribute. Two engine realities drive this:

* **`@include` cannot resolve a backtick attribute name.** `@include %!/INCLUDE\`CAPTURE`
  silently no-ops (only `get()`/`u()` resolve `FOO\`BAR` tree attributes), so a
  factored tail never runs — nothing is captured. Inlining avoids `@include`.
* **A multi-line `$`-command body misparses its first command**, and q-registers
  do not cross newlines in a `$`-command body. The single-line `;` form is robust.

```mush
@@ Built-in POSE emits "<name> <message>" to the room, then records the pose.
&CMD`CAPTURE`POSE Scene Logger=$pose *: @emit [name(%#)] %0; @assert words(scenewhere(%L)); @assert strmatch(scenefocus(%#),scenewhere(%L)); @scene/addpose [scenewhere(%L)]=%#,[scenemember(scenewhere(%L),%#,showas)],%L,pose,,[name(%#)] %0

@@ Built-in SAY emits "You say, \"<msg>\"" to the speaker and
@@ "<name> says, \"<msg>\"" to everyone else — reproduce both, then record.
&CMD`CAPTURE`SAY Scene Logger=$say *: @pemit %#=You say, "%0"; @oemit %L/%#=[name(%#)] says, "%0"; @assert words(scenewhere(%L)); @assert strmatch(scenefocus(%#),scenewhere(%L)); @scene/addpose [scenewhere(%L)]=%#,[scenemember(scenewhere(%L),%#,showas)],%L,say,,[name(%#)] says\, "%0"

@@ Built-in SEMIPOSE emits "<name><message>" (no space), then records.
&CMD`CAPTURE`SEMI Scene Logger=$;*: @emit [name(%#)]%0; @assert words(scenewhere(%L)); @assert strmatch(scenefocus(%#),scenewhere(%L)); @scene/addpose [scenewhere(%L)]=%#,[scenemember(scenewhere(%L),%#,showas)],%L,semipose,,[name(%#)]%0
```

`scenewhere(%L)` resolves the room's active scene; `scenefocus(%#)` is the
poser's currently-focused scene (their `member` edge `isCurrent`). Requiring the
two to match means passers-by and people focused on a different scene are **not**
logged. `@scene/addpose <id>=<author>,<showAs>,<origin>,<source>,<tags>,<content>`
records the pose with an explicit author and publishes the realtime event.

### Web pose-authoring reuses this exact path

The portal's live-scene composer sends a normal `POSE`/`SAY`/`SEMIPOSE` through
`GameHub.SendCommand` on the player's play connection — so the **same** override
fires. There is **one** stored pose; the room emit and the `game.scene.{id}`
broadcast are two renderings of it (keyed by pose id). No double-capture, no echo
loop. The composer must never use `@emit`.

---

## 3. Player verbs (`+scene/*`)

Players never touch `@scene`. Default permission: **anyone may create/schedule
and becomes the owner; owner-only for lifecycle/management; authors edit their
own poses.**

All writes go through `@scene/*` commands; reads stay as `scene…()` functions.
`%!` is the Scene Logger object (the verbs live on it), so `u(%!/FUN\`OWNS,…)`
calls its guard.

> **Softcode conventions that matter here (engine realities):**
> * Each verb body is **one physical line** (`;`-separated). A multi-line
>   `$`-command body misparses its first command (the next line's leading
>   `@scene…` leaks into it) and q-registers do not cross newlines in a
>   `$`-command body — both break the verbs. Use `;`.
> * `@scene/*` are NoParse commands: they evaluate their own args, but a bare
>   `%0`/`%1` positional passed *directly* as an `@scene` arg can collide with
>   the command's own arg context. Stash the positional into a `%q` register
>   first when it would otherwise be the whole arg (see `EDIT`).
> * `@create`/`@assert setr(...)` in command position **does** propagate its
>   register to later `;` commands; a `setr()` inside an `&attr=<value>` RHS does
>   **not** — capture the scene id with `@assert setr(...)`, not `&MY.SID=[setr(...)]`.

```mush
@@ ---- create / lifecycle -------------------------------------------------
&CMD`CREATE Scene Logger=$+scene/create *: @assert setr(0,scenecreate(%L,%#,%0)); @scene/member %q0/owner=%#; @scene/focus %#=%q0; @scene/set %q0/status=[get(%!/DATA`DEFAULT_STATUS)]; @pemit %#=Scene %q0 created and focused.; &MY.SID %#=%q0

&CMD`START   Scene Logger=$+scene/start: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/set [scenefocus(%#)]/status=active; @pemit %#=Scene is now active.
&CMD`PAUSE   Scene Logger=$+scene/pause: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/set [scenefocus(%#)]/status=paused; @pemit %#=Scene paused.
&CMD`FINISH  Scene Logger=$+scene/finish: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/set [scenefocus(%#)]/status=finished; @scene/focus %#=; @pemit %#=Scene finished.

@@ ---- membership / RSVP / focus ------------------------------------------
&CMD`JOIN  Scene Logger=$+scene/join *: @scene/member %0/participant=%#; @scene/focus %#=%0; @pemit %#=Joined and focused on scene %0.
&CMD`LEAVE Scene Logger=$+scene/leave: @assert setr(0,scenefocus(%#)); @scene/focus %#=; @if not(words(sceneposes(%q0,%#)))={@scene/unmember %q0=%#}; @pemit %#=Left the scene.; &MY.SID %#=%q0
&CMD`TAG   Scene Logger=$+scene/tag *: @scene/member %0/attending=%#; @pemit %#=RSVP'd to scene %0.
&CMD`UNTAG Scene Logger=$+scene/untag *: @scene/unmember %0=%#; @pemit %#=Removed RSVP from scene %0.
&CMD`SHOWAS Scene Logger=$+scene/showas *: @scene/showas [scenefocus(%#)]/%#=%0; @pemit %#=Future poses show as: %0

@@ ---- edit your own poses -------------------------------------------------
@@ %1 is find^^^replace; author-only is enforced by @scene/editpose. The poseId
@@ (%0) is stashed in %q1 so it is not a bare positional inside the @scene arg.
&CMD`EDIT Scene Logger=$+scene/edit *=*: @assert setr(1,%0); @assert setr(0,edit(scenepose(scenefocus(%#),%q1,content),before(%1,^^^),after(%1,^^^))); @scene/editpose %q1=%#,%q0

&CMD`UNDO   Scene Logger=$+scene/undo *: @scene/undo %0
&CMD`REDO   Scene Logger=$+scene/redo *: @scene/redo %0
&CMD`DELETE Scene Logger=$+scene/delete *: @scene/delete %0
&CMD`MOVE   Scene Logger=$+scene/move *=*: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/move %0=%1

@@ ---- visibility ----------------------------------------------------------
&CMD`PUBLIC  Scene Logger=$+scene/public: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/set [scenefocus(%#)]/public=1; @pemit %#=Scene is now public.
&CMD`PRIVATE Scene Logger=$+scene/private: @assert u(%!/FUN`OWNS,%#,scenefocus(%#)); @scene/set [scenefocus(%#)]/public=0; @pemit %#=Scene is now private.

@@ ---- viewing --------------------------------------------------------------
&CMD`WHO   Scene Logger=$+scene/who *: @pemit %#=Cast: [scenecast(%0)]%r[iter(scenemembers(%0),[name(##)] ([scenemember(%0,##,role)]))]
&CMD`RECAP Scene Logger=$+scene/recap *: @pemit %#=[iter(sceneposes(scenefocus(%#),,%0),scenepose(scenefocus(%#),##,content),%b,%r)]
```

> The `+scene/schedule` and `+scene/create/temp` verbs from earlier drafts are not
> part of the shipped 1.0 package; add them by editing the package if you want
> roomless scheduling or temp-room staging (see §4).

### Owner / wizard guard

```mush
&FUN`OWNS Scene Logger=
  or(orflags(%0,Wz),strmatch(scene(%1,owner),%0))
```

---

## 4. Temp rooms — entirely softcode (optional extension)

When a scene wants its own staging room, softcode does the building; the engine
just flags the room. `@dig` the room, set the `SCENE_ROOM` flag (symbol `S`),
`@tel` joiners in, and `@destroy` it after the grace period once the scene
finishes. This is **not** in the shipped 1.0 package — add it by editing the
Scene Logger's managed attributes (via the package manager) if you want it:

```mush
&CMD`CREATE_TEMP Scene Logger=$+scene/create/temp *:
  @assert lte(u(%!/FUN`TEMP_COUNT,%#),get(%!/DATA`TEMP_MAX));
  @dig/teleport [setr(0,Scene: %0)] , , ;
  @set %q0=SCENE_ROOM;
  @chzone %q0=get(%!/DATA`TEMP_ZONE);
  &MY.SID %#=[setr(1,[scenecreate(%q0,%#,%0)])];
  @scene/set %q1/temproom=%q0;
  @scene/member %q1/owner=%#;
  @scene/focus %#=%q1

@@ on /finish of a temp scene, schedule cleanup after DATA`TEMP_GRACE_MIN:
@@   @wait [mul(60,get(%!/DATA`TEMP_GRACE_MIN))]=@destroy <temproom>
```

---

## 5. Verifying

```mush
@@ from a SCENE_ROOM with an active scene, after a pose:
think scenewhere(here)                 @@ -> the active scene id
think sceneposes(scenewhere(here))     @@ -> ordered pose ids
think scene(scenewhere(here),owner)    @@ -> the owner dbref (e.g. #123)
```

The portal `/scenes` browser, `/scenes/{id}` archive, and `/scenes/{id}/live`
feed all read the same data over `/api/scenes`, and the live page receives each
new pose over `game.scene.{id}` as it is captured.
