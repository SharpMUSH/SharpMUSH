using System.Text.Json.Serialization;
using DotNext.Threading;

namespace SharpMUSH.Library.Models;

public record SharpAttribute(
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string? LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<SharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry)
{
	public MString Value { get; set; } = MModule.empty();
}

public record LazySharpAttribute(
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string? LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<LazySharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry,
	[property: JsonIgnore] AsyncLazy<MString> Value);