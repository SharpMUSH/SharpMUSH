namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A symbolic object reference inside a package manifest (decision 20.11).
/// Packages never contain raw dbrefs — every object mention is a mustache
/// token resolved to a real dbref at install (apply) time:
/// <c>{{name}}</c> intra-package, <c>{{$name}}</c> well-known,
/// <c>{{?name}}</c> configure, <c>{{pkg/name}}</c> cross-package.
/// Names are case-insensitive and normalized to lowercase.
/// </summary>
/// <param name="Kind">How this reference is resolved.</param>
/// <param name="Name">The ref name, lowercase, without sigils or braces.</param>
/// <param name="Package">For cross-package refs, the dependency package id; otherwise null.</param>
public sealed record PackageRef(PackageRefKind Kind, string Name, string? Package = null)
{
	public override string ToString() => Kind switch
	{
		PackageRefKind.Internal when Package is not null => $"{{{{{Package}/{Name}}}}}",
		PackageRefKind.Internal => $"{{{{{Name}}}}}",
		PackageRefKind.WellKnown => $"{{{{${Name}}}}}",
		PackageRefKind.Configure => $"{{{{?{Name}}}}}",
		_ => Name
	};
}

/// <summary>The resolution strategy for a <see cref="PackageRef"/>.</summary>
public enum PackageRefKind
{
	/// <summary>
	/// <c>{{name}}</c> — an object defined in this package, or
	/// <c>{{pkg/name}}</c> — an object owned by a declared dependency;
	/// resolved to that object's dbref at apply time.
	/// </summary>
	Internal,

	/// <summary>
	/// <c>{{$name}}</c> — a standard object every MUSH has
	/// (e.g. <c>{{$room_zero}}</c>); resolved from server configuration.
	/// </summary>
	WellKnown,

	/// <summary>
	/// <c>{{?name}}</c> — a value the installing admin supplies during the
	/// review step; must be declared under <c>configure:</c>.
	/// </summary>
	Configure
}

/// <summary>
/// The default set of <c>{{$well-known}}</c> ref names resolvable from server
/// configuration. Servers may extend the set (service constructor).
/// </summary>
public static class WellKnownRefs
{
	public const string RoomZero = "room_zero";
	public const string MasterRoom = "master_room";
	public const string PlayerStart = "player_start";
	public const string God = "god";
	public const string PackageManager = "package_manager";
	public const string HttpHandler = "http_handler";

	/// <summary>All built-in well-known ref names (lowercase).</summary>
	public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
	{
		RoomZero,
		MasterRoom,
		PlayerStart,
		God,
		PackageManager,
		HttpHandler
	};
}
