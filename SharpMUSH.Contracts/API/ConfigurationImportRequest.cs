namespace SharpMUSH.Library.API;

/// <summary>
/// A PennMUSH-style configuration file for the server to apply, with the files its <c>include</c> lines name.
/// </summary>
/// <param name="Content">The configuration file's text.</param>
/// <param name="Includes">
/// The other files uploaded with it, by file name, with their text. An <c>include</c> line is matched by its file name
/// alone and is never read from the server's disk.
/// </param>
public record ConfigurationImportRequest(string Content, IReadOnlyDictionary<string, string>? Includes = null);
