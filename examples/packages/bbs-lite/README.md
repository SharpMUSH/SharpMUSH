# bbs-lite

A deliberately tiny bulletin board that exercises every manifest feature
beyond the basics:

- **Dependency with a range constraint and a source hint** — requires
  `who-where` at any 1.x version (`>=1.0 <2.0`). Install is blocked until it's
  present, but because the dependency declares a `source:` (repo, directory
  path, branch), the installer can offer to fetch it from there rather than
  just reporting it missing.
- **Cross-package ref** — the function object is `@parent`ed to
  `{{who-where/ww_functions}}`, an object owned by the dependency. Cross-package
  refs are only legal when the target package is declared under `depends:`.
- **Typed configure parameters** — `{{?bbs_storage}}` is a dbref the admin
  supplies during review; `{{?board_name}}` is a string with a default
  (`"Community Board"`). dbref-typed params can't declare defaults — dbrefs
  are game-specific, which is the point of configure.
- **`conflicts:`** — declares it can't coexist with `legacy-bbs`.
- **Well-known ref** — the lounge room is parented to `{{$room_zero}}`.
- **Lock with an internal ref** — the function object's use-lock points at
  `{{bbsl_global}}`, so only the global object can call its functions.
- **Attribute flags** — `BBSL_FN_LIST` carries `no_clone`.
- **Prerelease version** — `0.9.0-beta` sorts before `0.9.0` and is never
  selected by a release-only constraint like `>=0.9`.

Posts are stored as `POST_<timestamp>` attributes on the configured storage
object; `+bbread` lists them via `lattr()` with a `firstof()` fallback when
the board is empty.
