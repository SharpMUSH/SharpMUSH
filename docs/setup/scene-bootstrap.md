# Scene System — `#SCENELOGGER` Softcode Bootstrap

The C# Scene System ships only **mechanism**: storage (`ISceneService`), the
wizard `@SCENE` command, the `scene…` softcode functions, the `SCENE_ROOM` flag,
the `game.scene.{id}` realtime broadcast, and the portal. It ships **no policy** —
nothing captures a pose, decides who may start a scene, formats a room emit, or
spins up a temp room. That is all **game policy** and lives here, in softcode, on
a single wizard object conventionally named `#SCENELOGGER`.

There is intentionally **no `SceneOptions` config category**: every knob is
policy, so it is set as `&conf.*` attributes on `#SCENELOGGER` and read by the
verbs below. One place, not two.

> Style note: examples follow SharpMUSH softcode conventions — bare `%0`/`%#`/
> `%q<…>` substitutions are never wrapped in `[ ]` (brackets are for function
> calls only), and `firstof()` is preferred over nested `if()` chains.

---

## 1. Create the logger object

`#SCENELOGGER` **must be WIZARD** — it is the executor for the wizard-only
`@SCENE` command and the `WizardOnly` side-effect `scene…` functions.

```mush
@create Scene Logger
@set Scene Logger = WIZARD
@set Scene Logger = SAFE
&DESC Scene Logger=Captures poses into the scene record. Do not @destroy.
think Logger is [num(Scene Logger)]   @@ note the dbref; below assumes #SCENELOGGER
```

### Policy attributes (replaces config)

```mush
&conf.capture        #SCENELOGGER=1
&conf.default_status #SCENELOGGER=new
&conf.default_public #SCENELOGGER=0
&conf.max_recent     #SCENELOGGER=50
&conf.statuses       #SCENELOGGER=new scheduled active paused finished
&conf.temp_zone      #SCENELOGGER=#0
&conf.temp_grace_min #SCENELOGGER=30
&conf.temp_max       #SCENELOGGER=3
&conf.share_owner    #SCENELOGGER=1
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

```mush
@hook/override POSE     = #SCENELOGGER, cap.pose
@hook/override SAY      = #SCENELOGGER, cap.say
@hook/override SEMIPOSE = #SCENELOGGER, cap.semi

&cap.pose #SCENELOGGER=$pose *:
  @break strmatch(get(#SCENELOGGER/conf.capture),1)={@emit [name(%#)] %0};
  @emit [name(%#)] %0;                                   @@ reproduce the built-in we replaced
  @assert words(scenewhere(%L));                         @@ a scene is active in this room?
  @assert strmatch(scenefocus(%#),scenewhere(%L));       @@ poser is focused on THIS scene
  think sceneaddpose(scenewhere(%L),%#,scenemember(scenewhere(%L),%#,showas),%L,pose,,%0)

&cap.say #SCENELOGGER=$say *:
  @emit [name(%#)] says, "%0";
  @assert words(scenewhere(%L));
  @assert strmatch(scenefocus(%#),scenewhere(%L));
  think sceneaddpose(scenewhere(%L),%#,scenemember(scenewhere(%L),%#,showas),%L,say,,[name(%#)] says\, "%0")

&cap.semi #SCENELOGGER=$;*:
  @emit [name(%#)]%0;
  @assert words(scenewhere(%L));
  @assert strmatch(scenefocus(%#),scenewhere(%L));
  think sceneaddpose(scenewhere(%L),%#,scenemember(scenewhere(%L),%#,showas),%L,semipose,,[name(%#)]%0)
```

`scenewhere(%L)` resolves the room's active scene; `scenefocus(%#)` is the
poser's currently-focused scene (their `member` edge `isCurrent`). Requiring the
two to match means passers-by and people focused on a different scene are **not**
logged. `sceneaddpose(scene, author, showAs, origin, source, tags, content)`
returns the new pose id (ignored here).

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

```mush
@@ ---- create / lifecycle -------------------------------------------------
&do.create #SCENELOGGER=$+scene/create *:
  &my.sid %#=[scenecreate(%L,%#,%0)];
  think sceneaddmember(get(%#/my.sid),%#,owner);
  think scenesetfocus(%#,get(%#/my.sid));
  think sceneset(get(%#/my.sid),status,get(#SCENELOGGER/conf.default_status));
  @pemit %#=Scene [get(%#/my.sid)] created and focused.

&do.schedule #SCENELOGGER=$+scene/schedule *=*:
  &my.sid %#=[scenecreate(,%#,%0)];                      @@ roomless => scheduled
  think sceneset(get(%#/my.sid),status,scheduled);
  think sceneset(get(%#/my.sid),scheduledfor,[convtime(%1)]000);  @@ utc millis
  @pemit %#=Scheduled "%0" (#[get(%#/my.sid)]) for %1.

&do.start #SCENELOGGER=$+scene/start:
  @assert u(fn.owns,%#,scenefocus(%#));
  think sceneset(scenefocus(%#),status,active);
  @pemit %#=Scene is now active.

&do.pause #SCENELOGGER=$+scene/pause:
  @assert u(fn.owns,%#,scenefocus(%#));
  think sceneset(scenefocus(%#),status,paused)

&do.finish #SCENELOGGER=$+scene/finish:
  @assert u(fn.owns,%#,scenefocus(%#));
  think sceneset(scenefocus(%#),status,finished);
  think scenesetfocus(%#);
  @pemit %#=Scene finished.

@@ ---- membership / RSVP / focus ------------------------------------------
&do.join #SCENELOGGER=$+scene/join *:
  think sceneaddmember(%0,%#,participant);
  think scenesetfocus(%#,%0);
  @pemit %#=Joined and focused on scene %0.

&do.leave #SCENELOGGER=$+scene/leave:
  &my.sid %#=[scenefocus(%#)];
  think scenesetfocus(%#);
  @@ no-trace exit: if they authored no poses, drop the membership entirely
  @if not(words(sceneposes(get(%#/my.sid),%#)))=
    {think sceneunmember(get(%#/my.sid),%#)};
  @pemit %#=Left the scene.

&do.tag #SCENELOGGER=$+scene/tag *:
  think sceneaddmember(%0,%#,attending);             @@ RSVP = the "attending" role
  @pemit %#=RSVP'd to scene %0.

&do.untag #SCENELOGGER=$+scene/untag *:
  think sceneunmember(%0,%#)

&do.showas #SCENELOGGER=$+scene/showas *:
  think sceneshowas(scenefocus(%#),%#,%0);
  @pemit %#=Future poses show as: %0

@@ ---- edit your own poses -------------------------------------------------
&do.edit #SCENELOGGER=$+scene/edit *=*:
  @@ %1 is find^^^replace; author-only is enforced by sceneeditpose
  think sceneeditpose(%0,%#,edit(scenepose(scenefocus(%#),%0,content),before(%1,^^^),after(%1,^^^)))

&do.undo   #SCENELOGGER=$+scene/undo *:   think sceneundo(%0)
&do.redo   #SCENELOGGER=$+scene/redo *:   think sceneredo(%0)
&do.delete #SCENELOGGER=$+scene/delete *: think scenedelpose(%0)
&do.move   #SCENELOGGER=$+scene/move *=*:
  @assert u(fn.owns,%#,scenefocus(%#));
  think scenemovepose(%0,%1)

@@ ---- visibility ----------------------------------------------------------
&do.public  #SCENELOGGER=$+scene/public:  think sceneset(scenefocus(%#),public,1)
&do.private #SCENELOGGER=$+scene/private: think sceneset(scenefocus(%#),public,0)

@@ ---- viewing --------------------------------------------------------------
&do.who #SCENELOGGER=$+scene/who *:
  @pemit %#=Cast: [scenecast(%0)]%r[iter(scenemembers(%0),[name(##)] ([scenemember(%0,##,role)]))]

&do.recap #SCENELOGGER=$+scene/recap *:
  @pemit %#=[iter(sceneposes(scenefocus(%#),,%0),scenepose(scenefocus(%#),##,content),@,%r)]
```

### Owner / wizard guard

```mush
&fn.owns #SCENELOGGER=
  or(orflags(%0,Wz),strmatch(scene(%1,owner),%0))
```

---

## 4. Temp rooms — entirely softcode

When a scene wants its own staging room, softcode does the building; the engine
just flags the room. `@dig` the room, set the `SCENE_ROOM` flag (symbol `S`),
`@tel` joiners in, and `@destroy` it after the grace period once the scene
finishes.

```mush
&do.create_temp #SCENELOGGER=$+scene/create/temp *:
  @assert lte(u(fn.temp_count,%#),get(#SCENELOGGER/conf.temp_max));
  @dig/teleport [setr(0,Scene: %0)] , , ;
  @set %q0=SCENE_ROOM;
  @chzone %q0=get(#SCENELOGGER/conf.temp_zone);
  &my.sid %#=[scenecreate(%q0,%#,%0)];
  think sceneset(get(%#/my.sid),temproom,%q0);
  think sceneaddmember(get(%#/my.sid),%#,owner);
  think scenesetfocus(%#,get(%#/my.sid))

@@ on /finish of a temp scene, schedule cleanup after conf.temp_grace_min:
@@   @wait [mul(60,get(#SCENELOGGER/conf.temp_grace_min))]=@destroy <temproom>
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
