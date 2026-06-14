using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models;

/// <summary>
/// A portal role: a named, prioritised bundle of three-state permission grants that can be
/// assigned to accounts (Discord-style). Built-in roles (God/Wizard/…) carry
/// <see cref="IsSystem"/> = true and cannot be deleted or re-slugged, though their permissions
/// and priority may be edited. System data — never visible to softcode; travels with backups.
/// </summary>
public class SharpRole
{
	/// <summary>Provider storage id (e.g. "node_roles/&lt;slug&gt;"). Empty for unsaved roles.</summary>
	public string? Id { get; set; }

	/// <summary>Stable URL-safe key and identity. Built-in slugs match <see cref="PortalRole"/> names, lower-cased.</summary>
	public required string Slug { get; set; }

	/// <summary>Human-readable display name.</summary>
	public required string Name { get; set; }

	/// <summary>Optional hex color (e.g. "#5aa9ff") for the role chip.</summary>
	public string? Color { get; set; }

	/// <summary>Resolution priority — higher wins when roles disagree on a scope.</summary>
	public int Priority { get; set; }

	/// <summary>True for built-in roles that cannot be deleted or re-slugged.</summary>
	public bool IsSystem { get; set; }

	/// <summary>Per-scope stance. Absent keys are treated as <see cref="PermissionState.Inherit"/>.</summary>
	public Dictionary<string, PermissionState> Permissions { get; set; } = new();

	/// <summary>Creation time (unix ms).</summary>
	public long CreatedAt { get; set; }

	/// <summary>Last-update time (unix ms).</summary>
	public long UpdatedAt { get; set; }
}
