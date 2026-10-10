using System.Diagnostics.CodeAnalysis;

namespace SharpMUSH.Library.Definitions;

public static partial class ErrorMessages
{
	public static partial class Notifications
	{
		// --- Channel messages aligned with PennMUSH src/extchat.c (CHAT: prefix) ---
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotOnChannel = "CHAT: You are not on channel <{0}>.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelDescSet = "CHAT: Channel <{0}> description set.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelDescCleared = "CHAT: Channel <{0}> description cleared.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatResizingBuffer = "CHAT: Resizing buffer of channel <{0}>";
		public const string ChatGuestsCantModify = "CHAT: Guests may not modify channels.";
		public const string ChatGuestsCantJoin = "Guests are not allowed to join channels.";
		/// <summary>PennMUSH src/extchat.c:1237 / :1252 / :1290.</summary>
		public const string ChatInvalidTarget = "Invalid target.";
		/// <summary>PennMUSH src/extchat.c:1533 — <c>do_chat</c> / <c>do_cemit</c> type refusal.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatWrongTypeForChannel = "Sorry, you're not the right type to be on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1241 — <c>@channel/on</c> type refusal.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatWrongTypeOfThingForChannel = "Sorry, wrong type of thing for channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1267.</summary>
		public const string ChatJoinDenied = "Permission to join denied.";
		/// <summary>PennMUSH src/extchat.c:1355.</summary>
		public const string ChatJoinOverrideSelf =
			"CHAT: Warning: You don't meet channel join permissions! (joining anyway)";
		/// <summary>PennMUSH src/extchat.c:1263.</summary>
		public const string ChatJoinOverrideTarget =
			"CHAT: Warning: Target does not meet channel join permissions! (joining anyway)";
		/// <summary>PennMUSH src/extchat.c:1541.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotAllowedToSpeak = "Sorry, you're not allowed to speak on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1636.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotAllowedToCemit = "Sorry, you're not allowed to @cemit on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1545.</summary>
		public const string ChatNoSuchChannel = "CHAT: No such channel.";
		/// <summary>PennMUSH src/extchat.c:2002.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCannotHideOnChannel = "You are not permitted to hide on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1737.</summary>
		public const string ChatCannotCreateThatType = "You can't create channels of that type.";
		/// <summary>PennMUSH src/extchat.c:1833.</summary>
		public const string ChatCannotMakeThatType = "You can't make channels that type.";
		/// <summary>PennMUSH src/extchat.c:1836.</summary>
		public const string ChatChannelWillBeDisabled = "Warning: channel will be disabled.";
		/// <summary>PennMUSH src/extchat.c:1049 — <c>list_partial_matches</c>, the header the ambiguity
		/// refusal is followed by. Each matching channel name is appended after a space.</summary>
		public const string ChatPartialMatchesAre = "CHAT: Partial matches are:";
		/// <summary>PennMUSH src/extchat.c:1294.</summary>
		public const string ChatGuestsCantLeave = "Guests are not allowed to leave channels.";
		/// <summary>PennMUSH src/extchat.c:1332.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatAlreadyOnChannel = "CHAT: You are already on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1367.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatYouJoinChannel = "CHAT: You join channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1404.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatYouLeaveChannel = "CHAT: You leave channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1274 — sent to the player who did the joining.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatYouJoinTargetToChannel = "CHAT: You join {0} to channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1272 — sent to the player who was joined.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatJoinsYouToChannel = "CHAT: {0} joins you to channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1305 — sent to the player who did the removing.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatYouRemoveTargetFromChannel = "CHAT: You remove {0} from channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1303 — sent to the player who was removed.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatRemovesYouFromChannel = "CHAT: {0} removes you from channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1283 / :1309.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatTargetAlreadyOnChannel = "{0} is already on channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1310.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatTargetNotOnChannel = "{0} is not on channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:4072.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatRecallFromChannel = "CHAT: Recall from channel <{0}>";
		/// <summary>PennMUSH src/extchat.c:4092.</summary>
		public const string ChatEndRecall = "CHAT: End recall";
		/// <summary>PennMUSH src/extchat.c:4095.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatRecallEntireBuffer =
			"CHAT: To recall the entire buffer, use @chan/recall {0}=0";
		/// <summary>PennMUSH src/extchat.c:4062.</summary>
		public const string ChatNothingToRecall = "CHAT: Nothing to recall.";
		/// <summary>PennMUSH src/extchat.c:4051.</summary>
		public const string ChatMustBeAbleToJoinToRecall =
			"CHAT: You must be able to join a channel to recall from it.";
		/// <summary>PennMUSH src/extchat.c:4030 — <c>@channel/recall</c> with a non-numeric line count.</summary>
		public const string ChatHowManyLinesToRecall = "How many lines did you want to recall?";
		/// <summary>PennMUSH src/extchat.c:4009.</summary>
		public const string ChatWhichLineToStartRecall = "Which line do you want to start recall from?";
		/// <summary>PennMUSH src/extchat.c:2981.</summary>
		public const string ChatNoConnectedPlayersOnChannel = "There are no connected players on that channel.";
		/// <summary>PennMUSH src/extchat.c:2983.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatMembersOfChannelAre = "Members of channel <{0}> are:";
		/// <summary>PennMUSH src/extchat.c:2698 — the one-line form of @channel/list/quiet.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelList = "CHAT: Channel list: {0}";
		/// <summary>PennMUSH src/extchat.c:2696.</summary>
		public const string ChatNone = "(None)";

		// --- @channel/gag, /mute, /hide and /combine: PennMUSH do_chan_user_flags (src/extchat.c:1900-2050)
		/// <summary>PennMUSH src/extchat.c:1908.</summary>
		public const string ChatOnlyPlayersCanUseThat = "Only players can use that option.";
		/// <summary>PennMUSH src/extchat.c:1915.</summary>
		public const string ChatNotOnAnyChannels = "You are not on any channels.";
		/// <summary>PennMUSH src/extchat.c:1922.</summary>
		public const string ChatAllChannelsMuted = "All channels have been muted.";
		public const string ChatAllChannelsUnmuted = "All channels have been unmuted.";
		/// <summary>PennMUSH src/extchat.c:1926.</summary>
		public const string ChatHideOnAllChannels = "You hide on all the channels you can.";
		public const string ChatUnhideOnAllChannels = "You unhide on all channels.";
		/// <summary>PennMUSH src/extchat.c:1930.</summary>
		public const string ChatAllChannelsGagged = "All channels have been gagged.";
		public const string ChatAllChannelsUngagged = "All channels have been ungagged.";
		/// <summary>PennMUSH src/extchat.c:1934.</summary>
		public const string ChatAllChannelsCombined = "All channels have been combined.";
		public const string ChatAllChannelsUncombined = "All channels have been uncombined.";
		/// <summary>PennMUSH src/extchat.c:1963.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNoLongerHearConnections = "You will no longer hear connection messages on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:1969.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNowHearConnections = "You will now hear connection messages on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:2008.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNoLongerOnWhoList = "You no longer appear on channel <{0}>'s who list.";
		/// <summary>PennMUSH src/extchat.c:2014.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNowOnWhoList = "You now appear on channel <{0}>'s who list.";
		/// <summary>PennMUSH src/extchat.c:2023.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNoLongerHearMessages = "You will no longer hear messages on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:2029.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNowHearMessages = "You will now hear messages on channel <{0}>.";
		/// <summary>PennMUSH src/extchat.c:2038.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatConnectionsNowCombined = "Connect messages on channel <{0}> will now be combined with others.";
		/// <summary>PennMUSH src/extchat.c:2045.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatConnectionsNoLongerCombined = "Connect messages on channel <{0}> will no longer be combined with others.";

		// --- @channel/title: PennMUSH do_chan_title (src/extchat.c:3125-3185)
		/// <summary>PennMUSH src/extchat.c:3147.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNoTitleSetOn = "You have no title set on <{0}>.";
		/// <summary>PennMUSH src/extchat.c:3149.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatYourTitleOnIs = "Your title on <{0}> is '{1}'.";
		/// <summary>PennMUSH src/extchat.c:3160 — "(NoTitles) " is prepended on a NoTitles channel.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatTitleCleared = "Title cleared for {0}channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:3181.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatTitleSet = "Title set for {0}channel <{1}>.";
		/// <summary>PennMUSH src/extchat.c:3166.</summary>
		public const string ChatTitleTooLong = "Title too long.";
		/// <summary>PennMUSH src/extchat.c:3173.</summary>
		public const string ChatInvalidCharacterInTitle = "Invalid character in title.";
		/// <summary>PennMUSH src/extchat.c:2828.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCannotDecompile = "CHAT: You don't have permission to decompile <{0}>.";
		/// <summary>PennMUSH src/extchat.c:2247 — the refusal on @channel/wipe, verbatim.</summary>
		public const string ChatWipeThatSillyGrin = "CHAT: Wipe that silly grin off your face instead.";
		/// <summary>PennMUSH src/extchat.c:2251.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatChannelWiped = "CHAT: Channel <{0}> wiped.";
		/// <summary>PennMUSH src/extchat.c:2226 — told to each member that was removed.</summary>
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatRemovedAllUsers = "CHAT: {0} has removed all users from <{1}>.";
		/// <summary>PennMUSH src/extchat.c:1557.</summary>
		public const string ChatMustBeOnChannelToSpeak = "You must be on that channel to speak on it.";
		/// <summary>PennMUSH src/extchat.c:1560.</summary>
		public const string ChatMustStopGaggingToSpeak = "You must stop gagging that channel to speak on it.";
		/// <summary>PennMUSH src/extchat.c:1680.</summary>
		public const string ChatWhatToEmit = "What do you want to emit?";

		public const string ChatAlreadyExists = "CHAT: Channel already exists.";
		public const string ChatChannelCreationFailed = "CHAT: The channel could not be created.";
		public const string ChatInvalidChannelNameShort = "CHAT: Invalid channel name.";
		public const string ChatChannelCreated = "Channel has been created.";
		public const string ChatYesOrNoOnly = "CHAT: Yes or No are the only valid options.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatNotMemberFormat = "CHAT: You are not a member of {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCombinedChannelsOnFormat = "CHAT: Combined channels turned on for {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatCombinedChannelsOffFormat = "CHAT: Combined channels turned off for {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatAlreadyInGagStateFormat = "CHAT: You are already in that gag state on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatGaggedOnFormat = "CHAT: You have been gagged on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatUngaggedOnFormat = "CHAT: You have been ungagged on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatAlreadyInHideStateFormat = "CHAT: You are already in that hide state on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatHiddenOnChannelFormat = "CHAT: You have been hidden on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatUnhiddenOnChannelFormat = "CHAT: You have been unhidden on {0}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatPlayerRemovedFromChannelFormat = "CHAT: {0} has been removed from {1}.";
		[StringSyntax(StringSyntaxAttribute.CompositeFormat)]
		public const string ChatPlayerAddedToChannelFormat = "CHAT: {0} has been added to {1}.";
		public const string ChatChannelRenamed = "CHAT: Renamed channel.";
	}
}
