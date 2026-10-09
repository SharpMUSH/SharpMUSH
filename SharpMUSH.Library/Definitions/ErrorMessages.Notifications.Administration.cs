using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		// --- GAME: broadcast notifications (PennMUSH src/bsd.c, src/game.c, src/conf.c) ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameShutdownBy = "GAME: Shutdown by {0}";
		public const string GameShutdownExternal = "GAME: Shutdown by external signal";
		public const string GameSavingDatabase = "GAME: Saving database. Game may freeze for a few moments.";
		public const string GameSaveComplete = "GAME: Save complete.";
		public const string GameSaveIn1Minute = "GAME: Database save in 1 minute.";
		public const string GameSaveIn5Minutes = "GAME: Database save in 5 minutes.";
		public const string GameHasConnected = "has connected.";
		public const string GameHasReconnected = "has reconnected.";
		public const string GameHasDisconnected = "has disconnected.";
		public const string GameHasPartiallyDisconnected = "has partially disconnected.";
		public const string GameHasHiddenConnected = "has HIDDEN-connected.";
		public const string GameHasHiddenReconnected = "has HIDDEN-reconnected.";
		public const string GameHasHiddenDisconnected = "has HIDDEN-disconnected.";
		public const string GameHasPartiallyHiddenDisconnected = "has partially HIDDEN-disconnected.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameRebootBy = "GAME: Reboot w/o disconnect by {0}, please wait.";
		public const string GameRebootFinished = "GAME: Reboot finished.";
		public const string GameRebootFailed = "GAME: Reboot failed.";
		public const string GameDbSaveFailed = "GAME: ERROR! Database save failed!";
		public const string GameDbConsistencyCheck = "GAME: Performing database consistency check.";
		public const string GameDbConsistencyDone = "GAME: Database consistency check complete.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameSuspectCreated = "GAME: Suspect {0} created.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string GameSuspectActivity = "GAME: Suspect {0}";

		public const string GameRebootNoDisconnect = "GAME: Reboot w/o disconnect from game account, please wait.";

		// --- SOCKSET / @SOCKSET (PennMUSH src/bsd.c sockset()) ---
		public const string SocksetNotConnected = "You are not connected?";
		public const string SocksetNeedsOptionAndValue = "You must give an option and a value.";
		public const string SocksetSetWhatOption = "Set what option?";
		public const string SocksetWidthSet = "Width set.";
		public const string SocksetHeightSet = "Height set.";
		public const string SocksetWidthNeedsPositiveInteger = "Width expects a positive integer.";
		public const string SocksetHeightNeedsPositiveInteger = "Height expects a positive integer.";
		public const string SocksetTerminalTypeSet = "Terminal Type set.";
		public const string SocksetPromptNewlinesOn = "A newline will be sent after a prompt.";
		public const string SocksetPromptNewlinesOff = "No newline will be sent after a prompt.";
		public const string SocksetStripAccentsOn = "Accents will be stripped.";
		public const string SocksetStripAccentsOff = "Accents will not be stripped.";
		public const string SocksetUnknownColorStyle =
			"Unknown color style. Valid color styles: 'auto', 'plain', 'hilite', '16color', 'xterm256', 'truecolor'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetColorStyleSetFormat = "Colorstyle set to '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetHyperlinksSetFormat = "Hyperlinks set to '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetCommandLinksSetFormat = "Command links set to '{0}'";
		public const string SocksetUnknownLinkSetting = "Unknown setting. Valid settings: 'on', 'off', 'auto'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetGraphicsSetFormat = "Graphics set to '{0}'";
		public const string SocksetUnknownGraphics =
			"Unknown graphics setting. Valid settings: 'auto', 'detect', 'kitty', 'iterm2', 'sixel', 'blocks', 'off'.";
		public const string SocksetGraphicsDetecting =
			"Asking your terminal what it can draw. Its answer arrives with the next line you send.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetAnimationSetFormat = "Animation set to '{0}'";
		public const string SocksetUnknownAnimationSetting = "Unknown setting. Valid settings: 'on', 'off'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetTerminalSetFormat = "Terminal set to '{0}'";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetUnknownTerminalFormat = "Unknown terminal '{0}'. Known terminals: {1}, or 'auto'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetCharsetSetFormat = "Charset set to '{0}'";
		public const string SocksetUnknownCharset = "Unknown charset. Valid settings: 'utf-8', 'latin-1', 'ascii', 'auto'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SocksetInvalidOptionFormat = "@sockset option '{0}' is not a valid option.";
		public const string SocksetInvalidDescriptor = "Invalid descriptor.";

		public const string LocaleSetFormat = "Locale set to {0}.";
		public const string LocaleCleared = "Locale cleared (reset to default).";
		public const string LocaleInvalidFormat = "Invalid locale: {0}.";
		public const string LocaleCurrentFormat = "Current locale: {0}.";
		public const string QuotaSystemDisabled = "The quota system is disabled on this server.";
		public const string QuotaSystemDisabledMessage = "Quota system disabled.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaStatusFormat = "Quota: {0}/{1}";
		public const string QuotaAmountMustBeNumber = "Quota amount must be a number.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MotdClearedFormat = "{0} MOTD cleared.";
		public const string MotdUsage = "Usage: @motd[/<type>] <message>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string MotdSetFormat = "{0} MOTD set.";

		public const string ConfigCantRemakeWorld = "You can't remake the world in your image.";
		public const string ConfigWhatToSet = "What did you want to set?";
		public const string ConfigCouldntSet = "Couldn't set that option.";
		public const string ConfigOptionSet = "Option set.";
		public const string ConfigOptionSetAndSaved = "Option set and saved.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionNotSettableFormat = "{0} cannot be set from inside the game.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigInvalidValueFormat = "'{1}' is not a valid value for {0}.";
		public const string ConfigOptionEnabled = "Enabled.";
		public const string ConfigOptionDisabled = "Disabled.";
		public const string ConfigCategoriesHeader = "Configuration categories";
		public const string ConfigAllCategories = "All categories";
		public const string ConfigUseCategoryHelp = "Use '@config <category>' to see options in a category.";
		public const string ConfigUseOptionHelp = "Use '@config <option>' to see the value of an option.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigNoOptionsInCategoryFormat = "No options found in category '{0}'.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionsInCategoryFormat = "Configuration: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigOptionValueFormat = " {0,-40} {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ConfigNoCategoryOrOptionFormat = "No configuration category or option named '{0}'.";

		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsObjectCountsFormat = "{0} objects = {1} rooms, {2} exits, {3} things, {4} players.";
		/// <summary>PennMUSH <c>do_stats</c> (<c>src/wiz.c:779</c>): the whole database, garbage included.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsWorldCountsFormat = "{0} objects = {1} rooms, {2} exits, {3} things, {4} players, {5} garbage.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsNoSuchPlayerFormat = "{0}: No such player.";
		public const string StatsNeedSearchWarrant = "You need a search warrant to do that!";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsFlagspaceHeaderFormat = "Stats for flagspace {0}:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsFlagspaceEntriesFormat = "  {0} entries in flag table.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsFlagspaceFlagsetsFormat = "  {0} different flagsets in use. {1} objects with no flags set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsFlagspaceMostCommonFormat = "  {0} objects share the most common set of flags.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsFlagspaceUniqueFormat = "  {0} objects have unique flagsets.";
		public const string StatsTablesHeader = "Table        Entries";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsTablesRowFormat = "{0,-12} {1,7}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StatsChunksUnsupportedFormat = "@stats/{0}: SharpMUSH has no chunk allocator. Attributes live in the database provider, which keeps no equivalent counters.";
		public const string WizMotdCleared = "Wizard MOTD cleared.";
		public const string WizMotdUsage = "Usage: @wizmotd <message>";
		public const string WizMotdSet = "Wizard MOTD set.";

		public const string BootPortUsage = "Usage: @boot/port <descriptor number>";
		public const string BootDescriptorMustBeNumber = "Descriptor number must be a number.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BootNoSuchDescriptorFormat = "No such descriptor: {0}.";
		public const string BootUsage = "Usage: @boot <player> | @boot/me | @boot/port <descriptor>";

		public const string ShutdownOnlyGodPanic = "Only God can perform a panic shutdown.";
		public const string ShutdownPanicInitiated = "PANIC SHUTDOWN initiated by God.";
		public const string ShutdownRebootInitiated = "Restarting the game engine. Connections stay open, and the server's supervisor starts it again.";
		public const string ShutdownRebootPending = "A restart is already under way.";
		public const string ShutdownParanoidInitiated = "PARANOID SHUTDOWN initiated.";
		public const string ShutdownParanoidDatabase = "Database state is continuously persisted.";
		public const string ShutdownInitiated = "SHUTDOWN initiated.";
		public const string ShutdownNoteWebApp = "Note: SharpMUSH runs as a web application. Traditional shutdown is not applicable.";
		public const string ShutdownNoteOrchestration = "In cloud/container deployments, use your orchestration tools to manage server lifecycle.";
		public const string ShutdownNoteNoSave = "Database state is preserved automatically. No explicit save is needed.";

		/// <summary>Reported by <c>@backup</c> on a provider that cannot copy its own world; the reason
		/// comes from the provider, because they differ.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BackupUnavailableFormat = "@backup is not available here: {0}.";
		public const string BackupStarted = "Copying the world. The game keeps running; this may take a while.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BackupCompleteFormat = "Backup {0} written ({1}). Keeping {2}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BackupFailedFormat = "Backup failed: {0}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BackupListHeaderFormat = "Backups in {0}:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string BackupListRowFormat = "  {0}  {1}";
		public const string BackupListEmpty = "No backups have been taken yet.";

		// @storage: capacity (#1465) and history retention (#1464).
		public const string StorageHistoryNone = "No kind of history is installed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StorageHistoryArchiveFormat = "Purged records are archived to {0} before they are deleted.";
		public const string StorageHistoryNoArchive = "No archive is configured: a purged record survives only in backups taken before it was purged.";
		public const string StoragePurgeStarted = "Running a history retention pass. The game keeps running; it works in small batches.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StoragePurgedFormat = "  {0}: purged {1} records ({2}) in {3} batches.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StoragePurgeKeptFormat = "  {0}: keeps everything; nothing purged.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string StoragePurgeFailedFormat = "  {0}: stopped after {1} records ({2}): {3}";

		public const string QuotaSetUsage = "Usage: @quota/set <player>=<amount>";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaForPlayerSetFormat = "Quota for {0} set to {1}.";
		public const string QuotaListingHeader = "Quota listing for all players:";
		public const string QuotaListingColumnHeader = "Player                      Used/Quota";
		public const string QuotaListingSeparator = "=========================================";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaPlayerRowFormat = "{0} {1,4}/{2,-4}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string QuotaPlayerObjectsFormat = "{0}'s quota: {1}/{2} objects used.";

		public const string SitelockCheckRequiresHost = "@SITELOCK/CHECK requires a hostname or IP address.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockHostMatchesFormat = "Host '{0}' matches pattern '{1}' with options: {2}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockHostNoMatchFormat = "Host '{0}' does not match any sitelock rules (default access allowed).";
		public const string SitelockNameListHeader = "Any name matching these wildcard patterns is banned:";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockNameLockedFormat = "Name {0} locked.";
		public const string SitelockNameRemoved = "Name removed.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockNameNotBannedFormat = "No banned name pattern is {0}.";
		public const string SitelockBanRequiresPattern = "@SITELOCK/BAN requires a host pattern.";
		public const string SitelockRegisterRequiresPattern = "@SITELOCK/REGISTER requires a host pattern.";
		public const string SitelockRemoveRequiresPattern = "@SITELOCK/REMOVE requires a host pattern.";
		public const string SitelockInvalidSyntax = "Invalid @SITELOCK syntax. Use '@help @sitelock' for usage information.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockRuleAddedFormat = "Sitelock rule for '{0}' added: {1}";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string SitelockRuleRemovedFormat = "Sitelock rule for '{0}' removed.";
		public const string SitelockRuleNotFound = "No sitelock rule was found for that pattern.";

		public const string PollMessageCleared = "Poll message cleared.";
		public const string PollNoPollMessage = "No poll message is currently set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string PollCurrentMessageFormat = "Current poll: {0}";
		public const string PollMessageSet = "Poll message set.";
	}
}
