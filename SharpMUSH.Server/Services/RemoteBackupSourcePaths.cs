namespace SharpMUSH.Server.Services;

/// <summary>Authoritative plugin, wiki and key-ring paths; deployments map these exact roots into the read-only worker.</summary>
public sealed record RemoteBackupSourcePaths(string PluginsPath, bool PluginsExplicitlyConfigured, string KeyRingPath,
	string WikiPath = "/app/data/wiki-assets", bool WikiExplicitlyConfigured = false);
