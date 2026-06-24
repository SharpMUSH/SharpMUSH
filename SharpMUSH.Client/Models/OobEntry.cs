namespace SharpMUSH.Client.Models;

/// <summary>A single clickable sidebar entry pushed by softcode (shape is softcode-defined).</summary>
public sealed record OobEntry(string Dbref, string Name, string? Cmd);
