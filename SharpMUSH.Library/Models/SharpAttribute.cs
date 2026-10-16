using DotNext.Threading;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public record SharpAttribute(
	string Id,
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<SharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry)
{
	[JsonIgnore]
	public StoredMarkup? StoredValue { get; init; }

	/// <summary>The metadata exists, but its stored body cannot be decoded. Never treat this as an absent attribute.</summary>
	[JsonIgnore]
	public bool IsUnreadable { get; init; }

	public MString Value { get; set; } = MarkupText.Empty;
}

public record LazySharpAttribute(
	string Id,
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<LazySharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry,
	[property: JsonIgnore] AsyncLazy<MString> Value);