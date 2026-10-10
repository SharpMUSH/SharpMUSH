using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		public const string FunctionLibraryUnavailable = "Function library unavailable.";
		/// <summary>PennMUSH <c>mailfun_fetch</c> (<c>src/extmail.c:2174</c>), unpunctuated, for a mailbox the caller does not control.</summary>
		public const string MailFetchPermissionDenied = "Permission denied";
		/// <summary>PennMUSH <c>mailfun_fetch</c> (<c>src/extmail.c:2180</c>), for a message that does not parse.</summary>
		public const string MailInvalidMessageSpecification = "Invalid message specification";
		public const string FunctionGlobalUserDefinedHeader = "Global user-defined functions:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionUserDefinedCountFormat = "  User-defined: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionEntryFormat = "    {0}: {1}-{2} args, Flags: {3}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionAndMoreFormat = "    ... and {0} more";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionBuiltInCountFormat = "  Built-in: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionUserDefinedSummaryFormat = "  {0} user-defined functions";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionBuiltInSummaryFormat = "  {0} built-in functions";
		public const string FunctionMustSpecifyName = "You must specify a function name.";
		public const string FunctionMustSpecifyAliasName = "You must specify an alias name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionAliasWouldCreateFormat = "@function/alias: Would create alias '{0}' for function '{1}'.";
		public const string FunctionAliasingNotImplemented = "Note: Function aliasing not yet implemented.";
		public const string FunctionMustSpecifyCloneName = "You must specify a clone name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionCloneWouldCloneFormat = "@function/clone: Would clone function '{0}' as '{1}'.";
		public const string FunctionCloningNotImplemented = "Note: Function cloning not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDeleteWouldDeleteFormat = "@function/delete: Would delete function '{0}'.";
		public const string FunctionDeletionNotImplemented = "Note: Function deletion not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDisableWouldDisableFormat = "@function/disable: Would disable function '{0}'.";
		public const string FunctionDisablingNotImplemented = "Note: Function disabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionEnableWouldEnableFormat = "@function/enable: Would enable function '{0}'.";
		public const string FunctionEnablingNotImplemented = "Note: Function enabling not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictWouldRestrictFormat = "@function/restrict: Would restrict function '{0}' to: {1}";
		public const string FunctionRestrictionNotImplemented = "Note: Function restriction not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictedFormat = "Function '{0}' restricted to: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictionClearedFormat = "Restriction cleared on function '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionArgumentNamesFormat = "Function '{0}' passes its arguments as: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionArgumentNamesClearedFormat = "Function '{0}' passes its arguments by position only.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionClonedFormat = "Function '{0}' cloned from '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionBuiltinRestoredFormat = "Function '{0}' restored to its built-in implementation.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionPreservedFormat = "Function '{0}' marked as preserved.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestoredOneFormat = "Function '{0}' restored to its built-in implementation.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestoredResetFormat = "@function/restore: removed {0} unpreserved user function(s); preserved entries kept.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDefineWouldDefineFormat = "@function: Would define function '{0}' as: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionMinArgsFormat = "  Min args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionMaxArgsFormat = "  Max args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionRestrictionsArgFormat = "  Restrictions: {0}";
		public const string FunctionDynamicDefinitionNotImplemented = "Note: Dynamic function definition not yet implemented.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionNotFoundFormat = "Function '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoNameFormat = "Function: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoTypeFormat = "  Type: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoMinArgsFormat = "  Min Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoMaxArgsFormat = "  Max Args: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoArgumentNamesFormat = "  Arguments: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoFlagsFormat = "  Flags: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionInfoRestrictionsFormat = "  Restrictions: {0}";
	}
}
