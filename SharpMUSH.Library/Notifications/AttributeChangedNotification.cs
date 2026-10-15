using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Published by the attribute write handlers after a write changed an attribute flagged <c>watched</c>: set,
/// cleared or wiped, by any command or function. Attributes without the flag publish nothing.
/// </summary>
/// <param name="Target">The object the attribute is on, as the write named it.</param>
/// <param name="Attribute">The attribute's full name, upper case.</param>
/// <param name="OldValue">Its value before the write; empty when the write created it.</param>
/// <param name="NewValue">Its value after the write; empty when the write cleared or removed it.</param>
/// <param name="Enactor">Whoever wrote it, or null for the server's own bookkeeping.</param>
public record AttributeChangedNotification(
	DBRef Target,
	string Attribute,
	MString OldValue,
	MString NewValue,
	DBRef? Enactor) : INotification;
