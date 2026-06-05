namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Atomic permissions for portal RBAC.
/// Each permission represents a specific capability in the web admin interface.
/// </summary>
[Flags]
public enum Permission
{
	/// <summary>No permissions.</summary>
	None = 0,

	// Admin Panel Access
	/// <summary>Access the admin panel dashboard.</summary>
	ViewAdminPanel = 1 << 0,

	// Account & User Management
	/// <summary>View all accounts and user details.</summary>
	ManageAccounts = 1 << 1,

	/// <summary>Create new accounts.</summary>
	CreateAccounts = 1 << 2,

	/// <summary>Edit account properties (username, email, password reset).</summary>
	EditAccounts = 1 << 3,

	/// <summary>Delete accounts.</summary>
	DeleteAccounts = 1 << 4,

	// Character & Portal Configuration
	/// <summary>Create scenes.</summary>
	CreateScene = 1 << 5,

	/// <summary>Edit scene properties.</summary>
	EditScene = 1 << 6,

	/// <summary>View private/restricted scenes.</summary>
	ViewPrivateScenes = 1 << 7,

	/// <summary>Delete scenes.</summary>
	DeleteScene = 1 << 8,

	// Portal Configuration
	/// <summary>Edit portal layout and styling.</summary>
	EditLayout = 1 << 9,

	/// <summary>Manage portal-wide settings and configuration.</summary>
	ManagePortalSettings = 1 << 10,

	// Package/Feature Management
	/// <summary>Manage packages, modules, and features.</summary>
	ManagePackages = 1 << 11,

	// Wiki Management
	/// <summary>Edit wiki pages.</summary>
	EditWiki = 1 << 12,

	/// <summary>Manage wiki structure and permissions.</summary>
	ManageWiki = 1 << 13,

	// Communication
	/// <summary>Send system mail or broadcast messages.</summary>
	SendMail = 1 << 14,

	/// <summary>Manage system channels and broadcast settings.</summary>
	ManageChannels = 1 << 15,

	// Logging & Auditing
	/// <summary>View system logs and audit trails.</summary>
	ViewLogs = 1 << 16,

	/// <summary>Export or download logs.</summary>
	ExportLogs = 1 << 17,

	// System Operations
	/// <summary>Restart or reload server components.</summary>
	ManageServer = 1 << 18,

	/// <summary>View and modify system database settings.</summary>
	ManageDatabase = 1 << 19,

	// Complete Admin Access
	/// <summary>Unrestricted access to all admin functions (God only).</summary>
	SuperAdmin = 1 << 20
}
