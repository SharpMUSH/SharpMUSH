using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		// --- Flag/Power messages aligned with PennMUSH src/flags.c ---
		// PennMUSH format: "AName(thing) - FLAGNAME set." / "AName(thing) - FLAGNAME reset."
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagSet = "{0} - {1} set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadySet = "{0} - {1} (already) set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagReset = "{0} - {1} reset.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadyReset = "{0} - {1} (already) reset.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontRecognizeFlag = "{0} - I don't recognize that flag.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string DontRecognizePower = "{0} - I don't recognize that power.";
		// PennMUSH set_power reports powers as granted/removed, not set/reset.
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerGranted = "{0} - {1} granted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAlreadyGranted = "{0} - {1} (already) granted.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerRemoved = "{0} - {1} removed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAlreadyRemoved = "{0} - {1} (already) removed.";

		// PennMUSH src/wiz.c do_power.
		public const string OnlyWizardsMayGrantPowers = "Only wizards may grant powers.";
		public const string GodIsAlreadyAllPowerful = "God is already all-powerful.";
		public const string MustSpecifyPowerToSet = "You must specify a power to set.";
		// PennMUSH src/flags.c do_flag_info, with the flagspace name lowercased.
		public const string NoSuchPowerInfo = "No such power.";
		public const string FlagAddRequiresNameAndSymbol = "@FLAG/ADD requires flag name and symbol.";
		public const string FlagNameAndSymbolCannotBeEmpty = "Flag name and symbol cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAlreadyExistsFormat = "Flag '{0}' already exists.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagCreatedWithSymbolFormat = "Flag '{0}' created with symbol '{1}'.";
		public const string FlagDeleteRequiresName = "@FLAG/DELETE requires a flag name.";
		public const string FlagNameCannotBeEmpty = "Flag name cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagNotFoundFormat = "Flag '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDeletedFormat = "Flag '{0}' deleted.";
		// PennMUSH src/flags.c do_flag_letter, with the FLAG flagspace name.
		public const string FlagLetterRequiresName = "@FLAG/LETTER requires a flag name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagLetterSetFormat = "Letter for flag {0} set to '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagLetterClearedFormat = "Letter for flag {0} cleared.";
		public const string FlagCharactersMustBeSingleCharacters = "Flag characters must be single characters.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagLetterConflictFormat = "Letter conflicts with the {0} flag.";
		public const string FlagTypeRequiresNameAndTypes = "@FLAG/TYPE requires flag name and type restrictions.";
		public const string FlagNameAndTypesCannotBeEmpty = "Flag name and types cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagTypeUpdatedFormat = "Flag '{0}' type restrictions updated to: {1}.";
		public const string FlagAliasRequiresNameAndAliases = "@FLAG/ALIAS requires flag name and aliases.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAliasesSetFormat = "Flag '{0}' aliases set to: {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagAliasConflictFormat = "That alias already matches the {0} flag.";
		public const string FlagRestrictRequiresNameAndPermissions = "@FLAG/RESTRICT requires flag name and permissions.";
		public const string FlagNameAndPermissionsCannotBeEmpty = "Flag name and permissions cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagPermissionsUpdatedFormat = "Flag '{0}' permissions updated to: {1}.";
		public const string FlagDecompileRequiresName = "@FLAG/DECOMPILE requires a flag name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDisabledFormat = "Flag '{0}' disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagEnabledFormat = "Flag '{0}' enabled.";

		public const string PowerAddRequiresNameAndAlias = "@POWER/ADD requires power name and alias.";
		public const string PowerNameAndAliasCannotBeEmpty = "Power name and alias cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAlreadyExistsFormat = "Power '{0}' already exists.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerCreatedWithAliasFormat = "Power '{0}' created with alias '{1}'.";
		public const string PowerDeleteRequiresName = "@POWER/DELETE requires a power name.";
		public const string PowerNameCannotBeEmpty = "Power name cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerNotFoundFormat = "Power '{0}' not found.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDeletedFormat = "Power '{0}' deleted.";
		public const string PowerAliasRequiresNameAndAlias = "@POWER/ALIAS requires power name and new alias.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAliasChangedFormat = "Power '{0}' alias changed to '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerAliasConflictFormat = "That alias already matches the {0} power.";
		public const string FlagDebugRequiresName = "@FLAG/DEBUG requires a flag name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string FlagDisableEnableRequiresNameFormat = "@FLAG/{0} requires a flag name.";
		public const string FlagUsage = "Usage: @flag/list, @flag/add <name>=<symbol>, @flag/delete <name>, @flag/letter <name>[=<letter>], @flag/type <name>=<types>, @flag/alias <name>=<aliases>, @flag/restrict <name>=<permissions>, @flag/decompile <name>";
		public const string PowerTypeRequiresNameAndTypes = "@POWER/TYPE requires power name and type restrictions.";
		public const string PowerNameAndTypesCannotBeEmpty = "Power name and types cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerTypeUpdatedFormat = "Power '{0}' type restrictions updated to: {1}.";
		public const string PowerRestrictRequiresNameAndPermissions = "@POWER/RESTRICT requires power name and permissions.";
		public const string PowerNameAndPermissionsCannotBeEmpty = "Power name and permissions cannot be empty.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerPermissionsUpdatedFormat = "Power '{0}' permissions updated to: {1}.";
		public const string PowerDecompileRequiresName = "@POWER/DECOMPILE requires a power name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDisableEnableRequiresNameFormat = "@POWER/{0} requires a power name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerDisabledFormat = "Power '{0}' disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerEnabledFormat = "Power '{0}' enabled.";
		// PennMUSH src/flags.c do_flag_letter.
		public const string PowerLetterRequiresName = "@POWER/LETTER requires a power name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerLetterSetFormat = "Letter for power {0} set to '{1}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerLetterClearedFormat = "Letter for power {0} cleared.";
		public const string PowerCharactersMustBeSingleCharacters = "Power characters must be single characters.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PowerLetterConflictFormat = "Letter conflicts with the {0} power.";
		public const string PowerUsage = "Usage: @power <power>, @power <object>=[!]<power> [[!]<power>...], @power/list, @power/add <name>=<alias>, @power/delete <name>, @power/alias <name>=<alias>, @power/letter <name>[=<letter>], @power/type <name>=<types>, @power/restrict <name>=<permissions>, @power/decompile <name>";
	}
}
