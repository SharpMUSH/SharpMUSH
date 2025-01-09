namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[property: PennConfig(Name = "connect_file")] string ConnectFile,
	[property: PennConfig(Name = "motd_file")] string MessageOfTheDayFile,
	[property: PennConfig(Name = "wizmotd_file")] string WizMessageOfTheDayFile,
	[property: PennConfig(Name = "newuser_file")] string NewUserFile,
	[property: PennConfig(Name = "register_create_file")] string RegisterCreateFile,
	[property: PennConfig(Name = "quit_file")] string QuitFile,
	[property: PennConfig(Name = "down_file")] string DownFile,
	[property: PennConfig(Name = "full_file")] string FullFile,
	[property: PennConfig(Name = "guest_file")] string GuestFile,
	[property: PennConfig(Name = "who_file")] string WhoFile,
	[property: PennConfig(Name = "connect_html_file")] string ConnectHtmlFile,
	[property: PennConfig(Name = "motd_html_file")] string MessageOfTheDayHtmlFile,
	[property: PennConfig(Name = "wizmotd_html_file")] string WizMessageOfTheDayHtmlFile,
	[property: PennConfig(Name = "newuser_html_file")] string NewUserHtmlFile,
	[property: PennConfig(Name = "register_create_html_file")] string RegisterCreateHtmlFile,
	[property: PennConfig(Name = "quite_html_file")] string QuitHtmlFile,
	[property: PennConfig(Name = "down_html_file")] string DownHtmlFile,
	[property: PennConfig(Name = "full_html_file")] string FullHtmlFile,
	[property: PennConfig(Name = "guest_html_file")] string GuestHtmlFile,
	[property: PennConfig(Name = "who_html_file")] string WhoHtmlFile,
	[property: PennConfig(Name = "index_html_file")] string IndexHtmlFile
);