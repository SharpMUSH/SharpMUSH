using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		public const string AttributeCannotBeChanged = "That attribute cannot be changed by you.";
		public const string AttributePermissionsCannotBeChanged = "That attribute's permissions cannot be changed.";
		/// <summary>
		/// PennMUSH's <c>AE_SAFE</c> wording (<c>src/set.c:1507-1509</c>), distinct from
		/// <c>AE_ERROR</c>'s <see cref="UnableToWipeAttribute"/>: <c>real_atr_clr</c>
		/// (<c>src/attrib.c:1100-1101</c>) tests <c>AF_Safe</c> before <c>Can_Write_Attr</c> and
		/// returns its own code, so the player is told which flag to clear rather than just that
		/// the wipe failed.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeIsSafeSetNotSafe = "Attribute {0} is SAFE. Set it !SAFE to modify it.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCannotBeWipedChildBlocked = "Attribute {0} cannot be wiped because a child attribute cannot be wiped.";
		/// <summary>
		/// PennMUSH's <c>af_helper</c> reports each half of a flag batch as ONE line naming the
		/// whole list - <c>"%s/%s - %s reset."</c> / <c>"%s/%s - %s set."</c>
		/// (<c>src/set.c:522-535</c>) - built from the REQUESTED bitmask, not from what actually
		/// changed, and suppressed entirely when <c>AreQuiet(player, thing)</c> or
		/// <c>AF_Quiet(atr)</c>. There is deliberately no "already set" or "is not set" wording:
		/// Penn has no such case.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeFlagsResetFormat = "{0}/{1} - {2} reset.";
		/// <inheritdoc cref="AttributeFlagsResetFormat"/>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeFlagsSetFormat = "{0}/{1} - {2} set.";
		/// <summary>
		/// <c>af_helper</c>'s refusal (<c>src/set.c:509-514</c>), one line per matched attribute the
		/// player may not change; the rest of the pattern's matches are still processed.
		/// </summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeFlagChangeDeniedFormat = "You cannot change that flag on {0}/{1}";
		/// <summary><c>do_attrib_flags</c> when no attribute was changed (<c>src/set.c:594-595</c>).</summary>
		public const string NoAttributeFoundToChange = "No attribute found to change.";

		// --- A player's ALIAS attribute: do_set_atr's ALIAS branch (PennMUSH src/attrib.c:2268-2316,
		// 2418-2423) and do_name's player case (src/set.c:83-100) ---
		public const string PlayerAliasSet = "Alias set.";
		public const string PlayerAliasRemoved = "Alias removed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerAliasNotValidFormat = "'{0}' is not a valid alias.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PlayerAliasTooManyFormat = "'{0}' contains too many aliases.";
		public const string PlayerAliasNull = "Null aliases are not valid.";
		public const string PlayerNameOrAliasNotAllowed = "You can't give a player that name or alias.";
		public const string PlayerNameTooManyAliases = "Too many aliases.";
		/// <summary>PennMUSH's <c>AE_NOTFOUND</c> (<c>src/attrib.c:2411-2412</c>).</summary>
		public const string NoSuchAttributeToReset = "No such attribute to reset.";

		// --- Attribute set messages aligned with PennMUSH src/set.c ---
		// PennMUSH format: "ObjectName/ATTRNAME - Set."
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeSet = "{0}/{1} - Set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCleared = "{0}/{1} - Cleared.";

		// PennMUSH src/atr_tab.c check_attr_value: a set refused by @attribute/enum or @attribute/limit.
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeValueNotInEnumFormat = "Value for {0} needs to be one of: {1}";
		public const string AttributeValueFailsLimit = "Attribute value does not match the /limit regexp.";
		public const string AttributeIsLocked = "That attribute is locked.";
		public const string AttributeIsUnlocked = "That attribute is unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeNotFoundOnSourceFormat = "Attribute {0} not found on source object.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCopiedToDestinationsFormat = "Attribute copied to {0} {1}.";
		public const string AttributeLocked = "Attribute locked.";
		public const string AttributeUnlocked = "Attribute unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeMovedFailedRemoveFormat = "Attribute moved to {0} {1} but failed to remove source: {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeMovedToFormat = "Attribute moved to {0} {1}.";
		// --- @cpattr/@mvattr with a wildcard source: one line per matched attribute, naming it ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeCopiedToDestinationsFormat = "Attribute {0} copied to {1} {2}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeMovedToFormat = "Attribute {0} moved to {1} {2}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeMovedFailedRemoveFormat = "Attribute {0} moved to {1} {2} but failed to remove source: {3}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PatternCopyDestinationNamesAttributeFormat = "Copying a pattern keeps each attribute's name; name only an object in {0}.";
		public const string AttributeNotFound = "No such attribute.";
		public const string AttributeOwnerChanged = "Attribute owner changed.";
		// --- @atrchown/@atrlock with a wildcard pattern: one line per matched attribute, naming it ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeOwnerChangedFormat = "Attribute {0} owner changed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NoPermissionToChownNamedFormat = "You don't have the permission to chown {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeIsLockedFormat = "Attribute {0} is locked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeIsUnlockedFormat = "Attribute {0} is unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeLockedFormat = "Attribute {0} locked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeUnlockedFormat = "Attribute {0} unlocked.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string NamedAttributeLockNeedsSetFormat = "You need to be able to set {0} to change its lock.";

		public const string AttributeCommandMustSpecifyAttribute = "You must specify an attribute.";
		public const string AttributeCommandMustSpecifyFlags = "You must specify attribute flags.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandUnknownFlagFormat = "Unknown attribute flag: {0}";
		public const string AttributeCommandFailedToCreate = "Failed to create attribute entry.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandPermissionsNowFormat = "{0} -- Attribute permissions now: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRetroactiveUpdatedFormat = "{0} existing copies of {1} updated.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRetroactivePartialFormat = "Stopped after {0} objects: {1} existing copies of {2} updated, {3} could not be. Run the command again to finish.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRemovedFromTableFormat = "Attribute '{0}' removed from standard attribute table.";
		public const string AttributeCommandExistingCopiesRemain = "Existing copies remain but are no longer \"standard\".";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNotFoundInTableFormat = "Attribute '{0}' not found in table.";
		public const string AttributeCommandMustSpecifyNewName = "You must specify a new name.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRenamedFormat = "Attribute '{0}' renamed to '{1}' in standard attribute table.";
		// PennMUSH src/atr_tab.c do_attribute_limit.
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRestrictionSetFormat = "{0} -- Attribute {1} set to: {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRestrictionUnsetFormat = "{0} -- Attribute limit or enum unset.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandRestrictionAlreadyUnsetFormat = "{0} -- Attribute limit or enum already unset.";
		public const string AttributeCommandNotInTableUseAccess = "I don't know that attribute. Please use @attribute/access to create it, first.";
		public const string AttributeCommandInvalidRegexp = "Invalid Regular Expression.";
		public const string AttributeCommandDelimiterOneCharacter = "Delimiter must be one character.";
		public const string AttributeCommandFailedToUpdate = "Failed to update attribute entry.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNotFoundNotErrorFormat = "Attribute '{0}' not found in standard attribute table.";
		public const string AttributeCommandNotFoundNotError2 = "This is not an error - the attribute may still be used on objects.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandInfoFormat = "@attribute: Information for '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDefaultFlagsFormat = "  Default flags: {0}";
		public const string AttributeCommandDefaultFlagsNone = "  Default flags: none";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandLimitPatternValueFormat = "  Limit pattern: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandEnumValuesFormat = "  Enum values: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandNoMatchPatternFormat = "No attributes match pattern '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileHeaderFormat = "@attribute/decompile: {0} attributes match pattern '{1}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileAccessFormat = "@attribute/access{0} {1}={2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileLimitFormat = "@attribute/limit {0}={1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string AttributeCommandDecompileEnumFormat = "@attribute/enum {0}={1}";
	}
}
