using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Services;

/// <summary>A plugin state result distinguishes dependency review from an ordinary refusal.</summary>
public union PluginStateChangeResult(PluginChangeResponse, PluginStateBlockers, ApiFailure);
