namespace SharpMUSH.Library.Models.Portal.Onboarding;

public sealed record GuideLesson(string Id, string Title, string Body, string? ActionLabel = null, string? ActionUrl = null, string? InsertCommand = null);
public sealed record GuideCommand(string Command, string Meaning, string Audience);

/// <summary>Reviewed, deterministic lessons. Examples never execute themselves.</summary>
public static class GuideCatalog
{
	public static IReadOnlyList<GuideLesson> Player(GuidePath path)
	{
		if (path == GuidePath.Mush) return Veteran;
		var lessons = Basics(path);
		if (path != GuidePath.OtherRoleplay) return lessons;
		return Array.AsReadOnly(lessons.Select(lesson => lesson.Id switch
		{
			"world" => lesson with { Body = "Unlike a forum thread, a room is a place your character occupies now. Read its description and exits to establish the scene. When a scene is available, Story view separates dialogue and actions; Terminal keeps the scrolling text interface. Both use the same character and connection." },
			"speak-act" => lesson with { Body = "As at a tabletop session, describe what your character attempts and leave other players room to respond. pose waves. adds your character's name; say Hello speaks to the current room. A private page reaches a named player. Agree on scene expectations and read this game's consent rules before joining." },
			_ => lesson
		}).ToArray());
	}

	private static IReadOnlyList<GuideLesson> Veteran { get; } = Array.AsReadOnly(new[]
	{
		new GuideLesson("place", "Just what's new", "Keep your MUSH knowledge: rooms, exits, objects and softcode still apply. This short tour covers the web account, character linking and the two Play views. Familiar command examples stay in the expandable reference; no command practice is required."),
		new GuideLesson("sign-in", "One account, your characters", "The web account is separate from your existing in-game character password. Sign in, then claim an existing character instead of recreating it. Select a linked character to play; never enter your account password in the command composer.", "Sign in or manage characters", "/login?returnUrl=%2Fwelcome"),
		new GuideLesson("world", "Story or Terminal: your choice", "Terminal retains the familiar scrolling interface. When a scene is available, Story offers separate Say, OOC, Emit and Command modes. Use Command for MUSH syntax and expressions; the other modes treat text literally. Switching views keeps the same game connection. Check your selected character before sending.", "Open Play", "/play"),
		new GuideLesson("channels", "Find people who can help", "Channels connect people across rooms. Use Browse channels in Play, or the commands below, to find a newbie, help or social channel. Names and access rules vary by game."),
		new GuideLesson("continue", "Explore this game's additions", "Optional applications and packages vary by game. Use the sections this game exposes and their own help; do not assume commands from another SharpMUSH game are installed here. The common-command reference remains available below. You can change your starting point for a fuller introduction.", "Explore the game", "/play")
	});

	private static IReadOnlyList<GuideLesson> Basics(GuidePath path) => Array.AsReadOnly(new[]
	{
		new GuideLesson("place", "A world you help write", path switch
		{
			GuidePath.Discord => "Imagine a roleplay server where your character can stand in a room, meet other characters, and leave through an exit. Rooms describe places in a shared world; they are not Discord channels. What you say usually reaches the people in your room.",
			GuidePath.Mush => "Your MUSH habits still apply: rooms, exits, objects, commands and softcode. SharpMUSH adds an account-based web portal and a choice of Story or Terminal views. Game-specific rules and packages still belong to this game.",
			GuidePath.OtherRoleplay => "Bring your roleplay experience. Here, you play a character in a persistent text world. Rooms establish the setting, exits connect places, and players write dialogue and actions together. Read this game's rules before joining a scene.",
			_ => "Explore at your own pace. This guide stays available from Help whenever you want a command reference or a hand finding your way."
		}),
		new GuideLesson("sign-in", "Your account opens the door", "Your account is your private sign-in. Your character is who other players meet. Sign in or register using the options this game offers; you do not type your account password into the game command box.", "Sign in or register", "/login?returnUrl=%2Fwelcome"),
		new GuideLesson("character", "Choose who you will play", "Use your existing character if you have one. If you played this game before, claim that character instead of creating a duplicate. Otherwise create a character when the game allows it. Your account can hold more than one; choose the one you want to play.", "My characters", "/account"),
		new GuideLesson("world", "Find your bearings", "Play opens a terminal for game commands. When a scene is available, Story view offers a structured view of that scene on the same connection. Check your current character and connection status. Look for the room description, who is here, and the exits. An exit moves your character; reading a description does not.", "Open Play", "/play"),
		new GuideLesson("useful-action", "Try one small command", "The look command asks the game to describe where you are. Try it in local practice first. Then insert look into the empty, connected Game command box (or Command mode in Story) and press Send yourself. Inserting it does not send or complete this step.", InsertCommand: "look"),
		new GuideLesson("speak-act", "Know who can hear you", "In the terminal, say Hello speaks to people in your room and pose waves. describes your character waving. Story view, when a scene is available, also offers Say, OOC and Emit modes that treat your text literally; Command runs game syntax and expressions. A page targets one named player. Practice here is local; check the game's etiquette before speaking live."),
		new GuideLesson("channels", "Find people who can help", "Channels connect people across rooms. Use Browse channels in Play, or the commands below, to find a newbie, help or social channel. Names and access rules vary by game."),
		new GuideLesson("continue", "You're ready to explore", "Use help for the game's command reference, look to reorient, and this game's rules and character guide for its own expectations. Ask the community when you need a hand. You can skip, close, or return to any part of this guide.", "Explore the game", "/play")
	});

	public static IReadOnlyList<GuideLesson> Administrator { get; } = Array.AsReadOnly(new[]
	{
		new GuideLesson("identity", "Introduce your game", "Set the game name, a short introduction, genre and Sharpie style. Keep your game's logo prominent. Save only when the preview says what you intend.", "Game identity", "/admin/onboarding"),
		new GuideLesson("joining", "Check how players join", "Review registration, guest access and character creation. Try the sign-in route you plan to share. These settings determine who can enter; the checklist does not change them.", "Player access settings", "/admin/config/Net"),
		new GuideLesson("rules", "Give players a clear starting point", "Publish readable welcome, rules and character guide pages, then link them here. Check them while signed out; private pages are deliberately omitted from the public guide.", "Open wiki", "/wiki"),
		new GuideLesson("capabilities", "Check what is installed", "Review your installed packages and their own help. A general scene or Discord feature does not prove a particular softcode command exists. Package installation still uses its normal review and confirmation.", "Review packages", "/admin/packages"),
		new GuideLesson("walkthrough", "Walk in as an ordinary player", "Try joining, choosing a character, look, help, and the four familiarity paths using an ordinary player account. An administrator preview cannot verify ordinary-player permissions.", "Preview player guide", "/welcome?preview=true"),
		new GuideLesson("invite", "Ready when you are", "Review completed and deferred items before sharing your game link. Finishing this checklist does not publish the game, open registration, install packages, or send invitations.")
	});

	public static IReadOnlyList<GuideCommand> Commands { get; } = Array.AsReadOnly(new[]
	{
		new GuideCommand("look", "Describe your current room", "Only you"),
		new GuideCommand("help", "Read command help", "Only you"),
		new GuideCommand("help say", "Read how a specific command works", "Only you"),
		new GuideCommand("inventory", "List what you carry", "Only you"),
		new GuideCommand("@channel/list", "Discover visible channels", "Only you"),
		new GuideCommand("@channel/on NAME", "Join a channel that permits you", "May announce your membership to the channel"),
		new GuideCommand("@chat NAME=Hello", "Talk on a channel", "People listening to that channel, wherever they are"),
		new GuideCommand("who", "See connected players the game lets you see", "Only you"),
		new GuideCommand("say Hello", "Speak as your character", "People in your room"),
		new GuideCommand("pose waves.", "Describe your character's action", "People in your room"),
		new GuideCommand("page NAME=Hello", "Send a private message", "One named player")
	});
}
