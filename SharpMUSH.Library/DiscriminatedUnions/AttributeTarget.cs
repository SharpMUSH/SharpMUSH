using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union for the target of attribute/validate operations,
/// replacing OneOf&lt;AnySharpObject, SharpAttributeEntry, SharpChannel, None&gt;.
/// </summary>
public union AttributeTarget(AnySharpObject, SharpAttributeEntry, SharpChannel, None)
{
	public bool IsAnySharpObject  => Value is AnySharpObject;
	public bool IsAttributeEntry  => Value is SharpAttributeEntry;
	public bool IsChannel         => Value is SharpChannel;
	public bool IsNone            => Value is null or None;

	public AnySharpObject    AsAnySharpObject  => (AnySharpObject)Value!;
	public SharpAttributeEntry AsAttributeEntry => (SharpAttributeEntry)Value!;
	public SharpChannel      AsChannel         => (SharpChannel)Value!;
}
