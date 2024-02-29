namespace AntlrCSharp.Implementation.Markup
{
	public abstract record IMarkup
	{
		public abstract string Wrap(string str);
	}
}