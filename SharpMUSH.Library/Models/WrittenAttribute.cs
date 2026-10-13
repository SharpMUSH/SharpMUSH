namespace SharpMUSH.Library.Models;

/// <summary>
/// What a successful value set left on the object, as far as its <c>"- Set."</c> report needs it.
/// </summary>
/// <param name="IsQuiet">
/// Whether the attribute now on the object carries <c>quiet</c> (PennMUSH's <c>AF_QUIET</c>,
/// <c>src/attrib.c:2446-2451</c>). A value set never changes an existing attribute's flags, and a new one
/// takes its attribute entry's defaults, so this is known without reading the attribute back.
/// </param>
public readonly record struct WrittenAttribute(bool IsQuiet);
