using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpMUSH.Implementation.Generated;

public static class Helper
{
	public static string GetFullClassName(MethodDeclarationSyntax method)
	{
		if (method.Parent is not ClassDeclarationSyntax classNode) throw new Exception("Method is not inside a class.");

		var ns = classNode.Parent;
		while (ns != null)
		{
			switch (ns)
			{
				case NamespaceDeclarationSyntax nds:
					return $"{nds.Name}.{classNode.Identifier.Text}";
				case FileScopedNamespaceDeclarationSyntax fs:
					return $"{fs.Name}.{classNode.Identifier.Text}";
				default:
					ns = ns.Parent;
					break;
			}
		}

		return classNode.Identifier.Text;
	}
}