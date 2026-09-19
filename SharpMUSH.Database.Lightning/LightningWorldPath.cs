namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The directory a host's Lightning world lives in, resolved once at registration. It is a service so a
/// host sharing a process with another can be given its own world: two LMDB environments on one path in
/// one process are unsafe.
/// </summary>
public sealed record LightningWorldPath(string Value);
