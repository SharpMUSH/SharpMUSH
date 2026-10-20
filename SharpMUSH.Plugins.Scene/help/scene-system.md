<!-- help-article
{
  "corpus": "help",
  "id": "scene-system",
  "lookup": "scene system",
  "aliases": ["scene plugin"],
  "sections": []
}
-->
# Scene System

The Scene System plugin keeps the game's scenes: who is in each one, the poses written in it and their order, every edit of a pose, and the plots scenes are filed under. The portal's scene pages read the same records.

Players use the scene package's `+scene` commands. Those commands do their work through the wizard-only `@scene` command and the scene functions below. Anyone can use `ooc`.

- [ooc]: speak out of character, and into your scene
- [@scene]: the wizard-only command the scene package runs
- [scene functions]: read scenes, poses and members
- [scene type functions]: read the game's pose types and scene details
- [scene write functions]: change scenes, poses, members and plots

Scene, pose and plot ids are numbers. Times are UTC milliseconds since 1970: [secs()] times 1000. When a scene is made or changed, the plugin raises the [scene events].
