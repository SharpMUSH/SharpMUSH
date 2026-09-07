namespace SharpMUSH.Library.Plugins.Storage.Lightning;

public enum TableKind { Node, ForwardEdge, ReverseEdge, Index }

/// <summary>One named LMDB sub-database. <see cref="Pair"/> links each edge direction to the other.</summary>
public sealed record TableDef(string Name, bool Duplicates, bool FixedDuplicates, TableKind Kind)
{
	public TableDef? Pair { get; internal set; }
	public override string ToString() => Name;

	// Pair is a circular back-reference (forward.Pair.Pair == forward): the compiler-generated
	// record Equals/GetHashCode would walk it and recurse forever. Identity is the name alone.
	public bool Equals(TableDef? other) => other is not null && Name == other.Name;
	public override int GetHashCode() => Name.GetHashCode();

	public static TableDef Node(string name) => new(name, false, false, TableKind.Node);
	public static TableDef Index(string name, bool duplicates = false) => new(name, duplicates, false, TableKind.Index);

	public static (TableDef Forward, TableDef Reverse) Edge(string name, bool fixedDuplicates)
	{
		var forward = new TableDef("e." + name, true, fixedDuplicates, TableKind.ForwardEdge);
		var reverse = new TableDef("r." + name, true, fixedDuplicates, TableKind.ReverseEdge);
		forward.Pair = reverse;
		reverse.Pair = forward;
		return (forward, reverse);
	}
}
