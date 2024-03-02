using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	/*
			accent()
			align()
			alphamax()
			alphamin()
			art()
			before()
			brackets()
			capstr()
			case()
			caseall()
			center()
			chr()
			comp()
			cond()
			condall()
			decode64()
			decompose()
			decrypt()
			digest()
			edit()
			encode64()
			encrypt()
			escape()
			flip()
			foreach()
			formdecode()
			hmac()
			if()
			ifelse()
			lcstr()
			left()
			ljust()
			lpos()
			merge()
			mid()
			ord()
			ordinal()
			pos()
			regedit()
			regmatch()
			repeat()
			right()
			rjust()
			scramble()
			secure()
			space()
			spellnum()
			squish()
			strallof()
			strdelete()
			strfirstof()
			strinsert()
			stripaccents()
			stripansi()
			strlen()
			strmatch()
			strreplace()
			switch()
			tr()
			trim()
			ucstr()
			urldecode()
			urlencode()
			wrap()
	*/

	public static partial class Functions
	{
		[PennFunction(Name = "after", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
		public static CallState After(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			var fullString = args[0]!.Message;
			var search = args[1]!.Message;
			var idx = MModule.indexOf(fullString, search);

			return new CallState(MModule.substring(idx, MModule.getLength(fullString) - idx, args[0].Message));
		}

		[PennFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
		public static CallState Concat(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args
					.Select(x => x.Message)
					.Aggregate((x,y) => MModule.concat(x,y)));

		[PennFunction(Name = "cat", Flags = FunctionFlags.Regular)]
		public static CallState Cat(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args
					.Select(x => x.Message)
					.Aggregate((x, y) => MModule.concat(x, y, MModule.single(" "))));

		[PennFunction(Name = "lit", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, MaxArgs = 1)]
		public static CallState Lit(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			throw new Exception("This should never get called. The FunctionParser should handle this.");
		}
	}
}
