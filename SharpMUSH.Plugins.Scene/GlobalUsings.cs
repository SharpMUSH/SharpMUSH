// Mirror the engine's global aliases so the moved scene command/function bodies compile unchanged
// (SharpMUSH.Implementation and SharpMUSH.Library define the same two aliases).
global using MModule = global::MarkupString.MarkupStringModule;
global using MString = global::MarkupString.MarkupString;
