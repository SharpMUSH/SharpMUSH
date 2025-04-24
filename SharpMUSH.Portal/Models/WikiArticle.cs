namespace SharpMUSH.Portal.Models;

public class WikiArticle
{
	public WikiArticle(string title, string content, string? image)
	{
		Title = title;
		Content = content;
		Image = image;
	}

	public string Title { get; set; }
	public string Content { get; set; }
	public string? Image { get; set; }

}
