using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		public const string CommandMustSpecifyName = "You must specify a command name.";
		public const string CommandMustSpecifyAlias = "You must specify an alias name.";
		public const string CommandMustSpecifyCloneName = "You must specify a clone name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandAddedFormat = "Command {0} added.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandAlreadyExistsFormat = "Command {0} already exists.";
		public const string CommandBadName = "Bad command name.";
		public const string CommandNoevalNoLongerNoparse = "WARNING: /NOEVAL no longer creates a Noparse command. Use /NOPARSE if that's what you meant.";
		public const string CommandAliasBadName = "I can't alias a command to that!";
		public const string CommandAliasFailed = "Unable to set alias.";
		public const string CommandAliasSet = "Alias set.";
		public const string CommandNoSuchCommand = "No such command.";
		public const string CommandCloned = "Command cloned.";
		public const string CommandCannotDeleteBuiltin = "You can't delete built-in commands. @command/disable instead.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandRemovedFormat = "Removed {0} from command table.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandRemovedWithAliasesFormat = "Removed {0} and aliases from command table.";
		public const string CommandHowToRestrict = "How do you want to restrict the command?";
		public const string CommandRestrictFailed = "Restrict attempt failed.";
		public const string CommandAlwaysEnabled = "@command is ALWAYS enabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandCalledByTheGameFormat = "{0} is run by the game itself and cannot be disabled.";
		public const string CommandNotImplemented = "This command has not been implemented.";
		public const string CommandLibraryUnavailable = "Command library unavailable.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandNotFoundFormat = "Command '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoNameFormat = "Name       : {0} ({1})";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoFlagsFormat = "Flags      : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoLockFormat = "Lock       : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoArgumentNamesFormat = "Arg names  : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandArgumentNamesFormat = "{0} hooks read its arguments as: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoFailureMsgFormat = "Failure Msg: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoSwitchesFormat = "Switches   : {0}";
		public const string CommandInfoNoSwitches = "Switches   :";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoLeftsideFormat = "Leftside   : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoRightsideFormat = "Rightside  : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoArgumentsFormat = "Arguments  : {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandInfoHookFormat = "@hook/{0}: #{1}/{2}";

		public const string HookMustSpecifyCommandName = "You must specify a command name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookNoHooksForCommandFormat = "No hooks set for command '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookListHeaderFormat = "Hooks for command '{0}':";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookEntryFormat = "  {0}: {1}/{2}{3}";
		public const string HookMustSpecifyType = "You must specify a hook type: /ignore, /override, /before, /after, or /extend";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookClearedFormat = "Hook '{0}' cleared for command '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookNotSetFormat = "No '{0}' hook set for command '{1}'.";
		public const string HookMustSpecifyObject = "You must specify an object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookAttributeNotFoundFormat = "Attribute '{0}' not found on object {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string HookSetFormat = "Hook '{0}' set for command '{1}'{2}.";

		/// <summary>
		/// PennMUSH src/command.c: <c>"%s doesn't know switch %s."</c>, notified in place of running the
		/// command. Only the first unknown switch is named, as PennMUSH names only the first.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string CommandUnknownSwitchFormat = "{0} doesn't know switch {1}.";

		public const string InputRescueUsage = "Usage: @input/rescue <player>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InputRescueNoneFormat = "{0} has no open input session.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string InputRescuedFormat = "Ended {0}'s input session. Its timeout callback runs now.";
	}
}
