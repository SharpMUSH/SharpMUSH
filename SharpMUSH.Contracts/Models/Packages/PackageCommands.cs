namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A command a package adds (<c>commands:</c>, format 1.24). The installer adds it as <c>@command/add</c> would and
/// hooks it with <c>@hook/override/inline/localize/clearregs</c> to <see cref="Attribute"/> on the package object
/// <see cref="Ref"/>; the server adds it again on every boot, and an uninstall removes it.
/// </summary>
/// <param name="Name">The command, upper case, such as <c>+TIME</c>.</param>
/// <param name="Ref">The manifest ref of the package object that holds the hooked attribute.</param>
/// <param name="Attribute">The attribute the hook runs, upper case, such as <c>CMD`TIME</c>.</param>
/// <param name="NoBreak">Whether the hook is <c>/nobreak</c>: an <c>@break</c> in it does not stop the code that ran the command.</param>
public sealed record PackageCommandSpec(string Name, string Ref, string Attribute, bool NoBreak = false);

/// <summary>An installed package's command, with the object it is hooked to (see <see cref="PackageCommandSpec"/>).</summary>
/// <param name="Name">The command, upper case.</param>
/// <param name="Ref">The manifest ref of the object the hook targets.</param>
/// <param name="Objid">That object's stable id (<c>#dbref:created-ms</c>).</param>
/// <param name="Attribute">The attribute the hook runs.</param>
/// <param name="NoBreak">Whether the hook is <c>/nobreak</c>.</param>
public sealed record PackageCommandRecord(string Name, string Ref, string Objid, string Attribute, bool NoBreak = false);

/// <summary>What happens to one command in a plan.</summary>
public enum PackageCommandAction
{
	/// <summary>The command is added and hooked.</summary>
	Add,

	/// <summary>The command stays as it is.</summary>
	Keep,

	/// <summary>The command stays, hooked to another object or attribute.</summary>
	Change,

	/// <summary>The package no longer declares the command, and it is removed.</summary>
	Remove,

	/// <summary>The command cannot be added; the apply is refused.</summary>
	Blocked
}

/// <summary>One command in a plan, shown on the review screen.</summary>
/// <param name="Name">The command.</param>
/// <param name="Action">What happens to it.</param>
/// <param name="Ref">The manifest ref of the object its hook runs on.</param>
/// <param name="Attribute">The attribute its hook runs.</param>
/// <param name="Detail">Why, for <see cref="PackageCommandAction.Blocked"/>.</param>
public sealed record PackageCommandChange(string Name, PackageCommandAction Action, string Ref, string Attribute, string? Detail = null);
