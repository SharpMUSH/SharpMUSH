// Mirror the engine's global aliases so the moved scene command/function bodies compile unchanged
// (SharpMUSH.Implementation and SharpMUSH.Library define the same two aliases).
global using MModule = global::MarkupString.MarkupStringModule;
global using MString = global::MarkupString.MarkupString;

// Phase 9: the scene contract surface (Scene/ScenePose/ScenePoseEdit/ScenePlot/SceneMember/
// SceneEventMessage/ISceneService) moved into SharpMUSH.Plugins.Scene.Contracts. A global using keeps the
// moved command/function/storage bodies compiling without per-file edits.
global using SharpMUSH.Plugins.Scene.Contracts;
// The `Scene` contract type collides by simple name with the `SharpMUSH.Plugins.Scene` namespace segment,
// so code that needs to disambiguate (e.g. method-parameter types) qualifies it as `Contracts.Scene`.
global using Contracts = SharpMUSH.Plugins.Scene.Contracts;
