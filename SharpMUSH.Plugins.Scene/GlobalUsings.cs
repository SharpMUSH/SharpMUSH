// Mirror the engine's global aliases so the moved scene command/function bodies compile unchanged
// (SharpMUSH.Implementation and SharpMUSH.Library define the same two aliases).
global using MModule = global::MarkupString.MarkupStringModule;
global using MString = global::MarkupString.MarkupString;

// The scene contract surface now lives ENTIRELY inside this plugin assembly (no shared Contracts assembly):
//   • the models (Scene/ScenePose/ScenePoseEdit/ScenePlot/SceneMember/SceneEventMessage) in .Models, and
//   • ISceneService alongside the storage in .Storage.
// Global usings keep the moved command/function/storage/web bodies compiling without per-file edits.
global using SharpMUSH.Plugins.Scene.Models;
global using SharpMUSH.Plugins.Scene.Storage;
// Shared object-reference resolution (here/me/name -> dbref via the engine LocateService) used by both
// the scene functions and the @scene command handlers.
global using SharpMUSH.Plugins.Scene.Common;
// The `Scene` model type collides by simple name with the `SharpMUSH.Plugins.Scene` namespace segment,
// so code that needs to disambiguate (e.g. method-parameter types) qualifies it as `Contracts.Scene`.
global using Contracts = SharpMUSH.Plugins.Scene.Models;
