namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[PennConfig(Name = "connect_file")] string ConnectFile,
	[PennConfig(Name = "motd_file")] string MessageOfTheDayFile,
	[PennConfig(Name = "wizmotd_file")] string WizMessageOfTheDayFile,
	[PennConfig(Name = "newuser_file")] string NewUserFile,
	[PennConfig(Name = "register_create_file")] string RegisterCreateFile,
	[PennConfig(Name = "quit_file")] string QuitFile,
	[PennConfig(Name = "down_file")] string DownFile,
	[PennConfig(Name = "full_file")] string FullFile,
	[PennConfig(Name = "guest_file")] string GuestFile,
	[PennConfig(Name = "who_file")] string WhoFile,
	[PennConfig(Name = "connect_html_file")] string ConnectHtmlFile,
	[PennConfig(Name = "motd_html_file")] string MessageOfTheDayHtmlFile,
	[PennConfig(Name = "wizmotd_html_file")] string WizMessageOfTheDayHtmlFile,
	[PennConfig(Name = "newuser_html_file")] string NewUserHtmlFile,
	[PennConfig(Name = "register_create_html_file")] string RegisterCreateHtmlFile,
	[PennConfig(Name = "quite_html_file")] string QuitHtmlFile,
	[PennConfig(Name = "down_html_file")] string DownHtmlFile,
	[PennConfig(Name = "full_html_file")] string FullHtmlFile,
	[PennConfig(Name = "guest_html_file")] string GuestHtmlFile,
	[PennConfig(Name = "who_html_file")] string WhoHtmlFile,
	[PennConfig(Name = "index_html_file")] string IndexHtmlFile
);