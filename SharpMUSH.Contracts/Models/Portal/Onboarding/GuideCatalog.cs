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
			"speak-act" => lesson with { Body = "As at a tabletop session, describe what your character attempts and leave other players room to respond. pose waves. adds your character's name; say Hello speaks to the current room. A private page reaches a named player. Keep out-of-character (OOC) talk marked apart from your character's words. Agree on scene expectations and read this game's consent rules before joining." },
			_ => lesson
		}).ToArray());
	}

	private static IReadOnlyList<GuideLesson> Veteran { get; } = Array.AsReadOnly(new[]
	{
		new GuideLesson("place", "Just what's new", "Keep your MUSH knowledge: rooms, exits, objects and softcode still apply. This short tour covers the web account, character linking and the two Play views. Familiar command examples stay in the expandable reference; no command practice is required."),
		new GuideLesson("sign-in", "One account, your characters", "The web account is separate from your existing in-game character password. Sign in, then claim an existing character instead of recreating it. Select a linked character to play; never enter your account password in the command composer.", "Sign in or manage characters", "/login?returnUrl=%2Fwelcome"),
		new GuideLesson("world", "Story or Terminal: your choice", "Terminal retains the familiar scrolling interface. When a scene is available, Story offers separate Say, OOC, Emit and Command modes. Use Command for MUSH syntax and expressions; the other modes treat text literally. Switching views keeps the same game connection. Check your selected character before sending.", "Open Play", "/play"),
		new GuideLesson("channels", "Find people who can help", "Find a help or newbie channel to ask other players a question."),
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
		new GuideLesson("speak-act", "Know who can hear you", "In the terminal, say Hello speaks to people in your room and pose waves. describes your character waving. Story view, when a scene is available, also offers Say, OOC and Emit modes that treat your text literally; Command runs game syntax and expressions. A page targets one named player. What your character says and does is in character (IC); talk about the game or the real world is out of character (OOC), so mark it the way this game asks, such as Story's OOC mode. Practice here is local; check the game's etiquette before speaking live."),
		new GuideLesson("channels", "Find people who can help", "Find a help or newbie channel to ask other players a question."),
		new GuideLesson("continue", "You're ready to explore", "Use help for the game's command reference, look to reorient, and this game's rules and character guide for its own expectations. Ask the community when you need a hand. You can skip, close, or return to any part of this guide.", "Explore the game", "/play")
	});

	public static IReadOnlyList<GuideLesson> Administrator { get; } = Array.AsReadOnly(new[]
	{
		new GuideLesson("identity", "Introduce your game", "Set the game name, a short introduction, genre and Sharpie style. Keep your game's logo prominent. Save only when the preview says what you intend.", "Game identity", "/admin/onboarding"),
		new GuideLesson("staff-character", "Keep #1 for emergencies", "The account you claimed plays #1, God. God passes every lock and permission check, so one mistyped @nuke or runaway softcode loop as God has nothing to stop it. Create a character of your own for everyday staff work, then, connected as #1, type @set *<name>=WIZARD. Connect as #1 only for what only God can do. Give other staff the narrowest role that fits their job, such as builder or moderator, rather than wizard.", "Create a staff character", "/characters/new"),
		new GuideLesson("joining", "Check how players join", "Review registration, guest access and character creation. Try the sign-in route you plan to share. These settings determine who can enter; the checklist does not change them.", "Player access settings", "/admin/config/Net"),
		new GuideLesson("start-room", "Prepare the room players arrive in", "New characters start in the player_start room, #0 on a new world, and look there is the first thing they read. Describe it, give it clear exits, and tell newcomers where help and the rules are. Keep the master room, #2 on a new world, for global commands only: no exits into it and no players inside.", "Core rooms", "/admin/config/Database"),
		new GuideLesson("rules", "Give players a clear starting point", "Publish readable welcome, rules and character guide pages, then link them here. Check them while signed out; private pages are deliberately omitted from the public guide.", "Open wiki", "/wiki"),
		new GuideLesson("channels", "Open channels for questions and staff", "A new world has no channels, but the player guide tells newcomers to find one. Create a public one for questions, for example @channel/add Newbie=player, and one for administrators, for example @channel/add Staff=admin, which only wizards and royalty can join. Ask your staff to join the public one so questions get answered."),
		new GuideLesson("messages", "Write the connect screen", "The connect screen is the first thing a MU* client shows, and the messages of the day greet everyone who connects. Name your game, say how to create a character, and remove offers your game does not make, such as guest logins when it has no guest characters.", "Game messages", "/admin/messages"),
		new GuideLesson("capabilities", "Check what is installed", "Review your installed packages and their own help. A general scene or Discord feature does not prove a particular softcode command exists. Package installation still uses its normal review and confirmation. If the Onboarding package is installed, add your game's own steps to its +welcome checklist.", "Review packages", "/admin/packages"),
		new GuideLesson("backups", "Back up before you open", "Run @backup once and check @backup/list shows the copy. Set SHARPMUSH_BACKUP_INTERVAL so copies are taken on a schedule, and keep a copy off this machine; a copy on the same disk does not survive losing it.", "Remote backups", "/admin/backups"),
		new GuideLesson("walkthrough", "Walk in as an ordinary player", "Try joining, choosing a character, look, help, and the four familiarity paths using a separate, ordinary account. A character on your own account shares that account's roles, and an administrator preview cannot verify ordinary-player permissions.", "Preview player guide", "/welcome?preview=true"),
		new GuideLesson("invite", "Ready when you are", "Review completed and deferred items before sharing your game link. Finishing this checklist does not publish the game, open registration, install packages, or send invitations.")
	});

	public static IReadOnlyList<GuideCommand> Commands { get; } = Array.AsReadOnly(new[]
	{
		new GuideCommand("look", "Describe your current room", "Only you"),
		new GuideCommand("help", "Read command help", "Only you"),
		new GuideCommand("help say", "Read how a specific command works", "Only you"),
		new GuideCommand("inventory", "List what you carry", "Only you"),
		new GuideCommand("@describe me=TEXT", "Set what others see when they look at you", "Anyone who looks at you"),
		new GuideCommand("home", "Go back to your home if you get lost", "People in the rooms you leave and arrive in"),
		new GuideCommand("@channel/list", "Discover visible channels", "Only you"),
		new GuideCommand("@channel/on NAME", "Join a channel that permits you", "May announce your membership to the channel"),
		new GuideCommand("@chat NAME=Hello", "Talk on a channel", "People listening to that channel, wherever they are"),
		new GuideCommand("who", "See connected players the game lets you see", "Only you"),
		new GuideCommand("say Hello", "Speak as your character", "People in your room"),
		new GuideCommand("pose waves.", "Describe your character's action", "People in your room"),
		new GuideCommand("page NAME=Hello", "Send a private message", "One named player")
	});
}
