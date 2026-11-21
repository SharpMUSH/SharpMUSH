namespace SharpMUSH.Library.API;

/// <summary>
/// An attribute table entry's keys, as <c>api/admin/attribute-keys</c> lists them: who may read and who may write that
/// attribute on every object although the usual rules refuse them.
/// </summary>
/// <param name="Name">The attribute's name in the table.</param>
/// <param name="ReadKey">The read key as a lock reads back (<c>ROLE^clerk</c>), or null for none.</param>
/// <param name="WriteKey">The write key, or null for none.</param>
public sealed record AttributeKeyEntryDto(string Name, string? ReadKey, string? WriteKey);

/// <summary>Sets one key of an attribute table entry; an empty or null <paramref name="Key"/> takes it off.</summary>
public sealed record SetAttributeKeyRequest(string? Key);
