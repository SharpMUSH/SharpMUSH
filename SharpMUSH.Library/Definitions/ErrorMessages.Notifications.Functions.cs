using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		public const string FunctionLibraryUnavailable = "Function library unavailable.";
		/// <summary>PennMUSH <c>mailfun_fetch</c> (<c>src/extmail.c:2180</c>), for a message that does not parse.</summary>
		public const string MailInvalidMessageSpecification = "Invalid message specification";
		public const string FunctionMustSpecifyName = "You must specify a function name.";
		public const string FunctionMustSpecifyAliasName = "You must specify an alias name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionAliasAddedFormat = "Alias '{0}' added for function '{1}'.";
		public const string FunctionMustSpecifyCloneName = "You must specify a clone name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDeletedFormat = "Function '{0}' deleted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionDisabledFormat = "Function '{0}' disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionEnabledFormat = "Function '{0}' enabled.";
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
		public const string FunctionDefinedFormat = "Function '{0}' defined as {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FunctionNotFoundFormat = "Function '{0}' not found.";
	}
}
