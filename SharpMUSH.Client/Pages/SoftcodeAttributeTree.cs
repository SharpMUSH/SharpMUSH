using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Pages;

/// <summary>One attribute of an object's attribute tree, with the attributes beneath it.</summary>
public sealed class SoftcodeAttributeNode(string path, int depth)
{
	/// <summary>The attribute's full name, e.g. <c>FOO`BAR</c>.</summary>
	public string Path { get; } = path;

	/// <summary>The last part of <see cref="Path"/>, e.g. <c>BAR</c>.</summary>
	public string Segment { get; } = SoftcodeAttributeTree.Segment(path);

	/// <summary>How many branches sit above this one; 0 at the top of the tree.</summary>
	public int Depth { get; } = depth;

	/// <summary>
	/// The attribute itself, or <c>null</c> when only attributes beneath it were listed: the listing stops
	/// at a depth and at what the reader may see, so a leaf can arrive without its branch.
	/// </summary>
	public MushAttribute? Attribute { get; internal set; }

	public List<SoftcodeAttributeNode> Children { get; } = [];

	public bool IsBranch => Children.Count > 0;

	/// <summary>Every attribute beneath this one, at any depth.</summary>
	public int DescendantCount => Children.Sum(c => 1 + c.DescendantCount);
}

/// <summary>
/// An object's attributes as the tree their names describe: <c>FOO`BAR`BAZ</c> is BAZ, under BAR,
/// under FOO.
/// </summary>
/// <remarks>
/// The attribute API lists a tree level by level rather than in tree order, so the editor cannot
/// render the list as it arrives. Nothing here touches the page, so the layout is testable without
/// rendering it.
/// </remarks>
public static class SoftcodeAttributeTree
{
	public const char Separator = '`';

	private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

	public static string Segment(string path)
	{
		var cut = path.LastIndexOf(Separator);
		return cut < 0 ? path : path[(cut + 1)..];
	}

	/// <summary>The branches above <paramref name="path"/>, outermost first: <c>FOO</c>, then <c>FOO`BAR</c> for <c>FOO`BAR`BAZ</c>.</summary>
	public static IEnumerable<string> Ancestors(string path)
	{
		for (var cut = path.IndexOf(Separator); cut > 0; cut = path.IndexOf(Separator, cut + 1))
		{
			yield return path[..cut];
		}
	}

	/// <summary>Arranges <paramref name="attributes"/> by name, siblings in alphabetical order.</summary>
	public static IReadOnlyList<SoftcodeAttributeNode> Build(IEnumerable<MushAttribute> attributes)
	{
		var roots = new List<SoftcodeAttributeNode>();
		var byPath = new Dictionary<string, SoftcodeAttributeNode>(NameComparer);

		SoftcodeAttributeNode NodeFor(string path, int depth)
		{
			if (byPath.TryGetValue(path, out var node))
			{
				return node;
			}

			node = new SoftcodeAttributeNode(path, depth);
			byPath[path] = node;
			var parent = path.LastIndexOf(Separator);
			(parent > 0 ? NodeFor(path[..parent], depth - 1).Children : roots).Add(node);
			return node;
		}

		foreach (var attribute in attributes)
		{
			NodeFor(attribute.Name, attribute.Name.Count(c => c == Separator)).Attribute = attribute;
		}

		Sort(roots);
		return roots;
	}

	private static void Sort(List<SoftcodeAttributeNode> nodes)
	{
		nodes.Sort((a, b) => NameComparer.Compare(a.Segment, b.Segment));
		foreach (var node in nodes)
		{
			Sort(node.Children);
		}
	}

	/// <summary>
	/// The rows to show, in tree order. With no <paramref name="filter"/>, a branch shows what is beneath
	/// it only when <paramref name="expanded"/> holds its path. With one, every attribute whose name
	/// contains it shows, along with the branches leading to it, whatever is expanded.
	/// </summary>
	public static IEnumerable<SoftcodeAttributeNode> Rows(
		IEnumerable<SoftcodeAttributeNode> nodes, IReadOnlySet<string> expanded, string? filter)
	{
		var filtering = !string.IsNullOrWhiteSpace(filter);
		foreach (var node in nodes.Where(node => !filtering || Matches(node, filter!)))
		{
			yield return node;

			if (filtering || expanded.Contains(node.Path))
			{
				foreach (var row in Rows(node.Children, expanded, filter))
				{
					yield return row;
				}
			}
		}
	}

	private static bool Matches(SoftcodeAttributeNode node, string filter) =>
		node.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Children.Any(c => Matches(c, filter));
}
