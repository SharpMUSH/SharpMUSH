namespace AntlrCSharp.Implementation.Markup
{
	public abstract record IMarkup
	{
		public string Wrap(string str)
		{
			return str;
		}
	}
}