namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The ref-indirection scheme (decision 20.21): installed softcode never
/// contains raw dbrefs. Each <c>{{ref}}</c> in an attribute value becomes
/// <c>[v(PM`REFS`NAME)]</c>, and the apply engine maintains a
/// <c>PM`REFS`NAME</c> attribute (an attribute tree) on every object whose
/// code uses the ref, holding the resolved value. Users can re-point a ref by
/// editing one attribute — and because the ref attrs are baseline-managed,
/// such local re-points survive upgrades as KeepLocal.
/// Structural fields (parent/location/destination) and lock strings are not
/// function-evaluated, so they keep direct dbref resolution.
/// </summary>
public static class PackageRefIndirection
{
	/// <summary>Root of the engine-managed attribute tree. Reserved: manifests may not define attributes under it.</summary>
	public const string AttributeTreeRoot = "PM";

	/// <summary>The branch holding ref values: <c>PM`REFS`NAME</c>.</summary>
	public const string RefsBranch = "PM`REFS";

	/// <summary>
	/// The attribute name a ref's value lives under. Same-package refs of any
	/// kind share one namespace (<c>PM`REFS`NAME</c> — manifest validation
	/// rejects cross-kind name collisions); cross-package refs are namespaced
	/// by the dependency id (<c>PM`REFS`WHO-WHERE`WW_FUNCTIONS</c>).
	/// </summary>
	public static string AttributeNameFor(PackageRef reference) =>
		reference is { Kind: PackageRefKind.Internal, Package: not null }
			? $"{RefsBranch}`{reference.Package.ToUpperInvariant()}`{reference.Name.ToUpperInvariant()}"
			: $"{RefsBranch}`{reference.Name.ToUpperInvariant()}";

	/// <summary>The softcode that recalls a ref's value at runtime.</summary>
	public static string IndirectionFor(PackageRef reference) =>
		$"[v({AttributeNameFor(reference)})]";

	/// <summary>
	/// Transforms MUSHcode for installation: every <c>{{ref}}</c> token
	/// becomes its <c>[v(PM`REFS`...)]</c> recall, and <c>{{{{</c> escapes
	/// become literal <c>{{</c>. Total — the output never contains ref tokens.
	/// </summary>
	public static string TransformCode(string value) =>
		PackageRefSubstitution.Substitute(value, reference => IndirectionFor(reference), out _);

	/// <summary>
	/// The distinct refs used in an object's attribute values, i.e. the
	/// <c>PM`REFS</c> entries that object needs.
	/// </summary>
	public static IReadOnlyList<PackageRef> RefsUsedIn(PackageObjectSpec spec) =>
		spec.Attributes.Values
			.SelectMany(attr => PackageRefScanner.Scan(attr.Value))
			.Where(token => token.Ref is not null)
			.Select(token => token.Ref!)
			.DistinctBy(reference => AttributeNameFor(reference))
			.OrderBy(reference => AttributeNameFor(reference), StringComparer.Ordinal)
			.ToList();

	/// <summary>True when an attribute name is inside the engine-reserved <c>PM</c> tree.</summary>
	public static bool IsReservedAttribute(string attributeName) =>
		attributeName.Equals(AttributeTreeRoot, StringComparison.OrdinalIgnoreCase)
		|| attributeName.StartsWith($"{AttributeTreeRoot}`", StringComparison.OrdinalIgnoreCase);
}
