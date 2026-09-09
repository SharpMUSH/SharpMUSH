using System.Reflection;
using System.Reflection.Emit;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class UserFunctionRegistryCompatibilityTests
{
	private static readonly (string Name, Type Result, Type[] Parameters)[] Legacy =
	[
		("Define", typeof(void), [typeof(UserDefinedFunction)]),
		("Resolve", typeof(UserDefinedFunction), [typeof(string)]),
		("Get", typeof(UserDefinedFunction), [typeof(string)]),
		("Delete", typeof(bool), [typeof(string)]),
		("SetEnabled", typeof(bool), [typeof(string), typeof(bool)]),
		("Alias", typeof(bool), [typeof(string), typeof(string)]),
		("SetRestriction", typeof(bool), [typeof(string), typeof(string)]),
		("Clone", typeof(bool), [typeof(string), typeof(string)]),
		("SetPreserved", typeof(bool), [typeof(string), typeof(bool)]),
		("ResetUnpreserved", typeof(int), []),
		("SetBuiltinRestriction", typeof(void), [typeof(string), typeof(string)]),
		("GetBuiltinRestriction", typeof(string), [typeof(string)]),
		("All", typeof(IReadOnlyCollection<UserDefinedFunction>), [])
	];

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublishedRegistrySlotsKeepTheirExactSignatures(bool concrete)
	{
		var type = concrete ? typeof(UserDefinedFunctionService) : typeof(IUserDefinedFunctionService);
		foreach (var (name, result, parameters) in Legacy)
			await Assert.That(type.GetMethod(name, parameters)?.ReturnType).IsEqualTo(result).Because(name);
	}

	[Test]
	public async Task DefinitionConstructorAndDeconstructionKeepTheirPublishedSignature()
	{
		Type[] parameters = [typeof(string), typeof(DBRef), typeof(string), typeof(int), typeof(int), typeof(bool), typeof(string), typeof(string), typeof(bool)];
		await Assert.That(typeof(UserDefinedFunction).GetConstructor(parameters)).IsNotNull();
		await Assert.That(typeof(UserDefinedFunction).GetMethod("Deconstruct", parameters.Select(type => type.MakeByRefType()).ToArray())).IsNotNull();
	}

	[Test]
	public async Task LegacyRegistryImplementationLoadsAndDispatchesWithoutNewOwnerFeatures()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LegacyRegistry" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
		var type = assembly.DefineDynamicModule("legacy").DefineType("LegacyImplementation", TypeAttributes.Public);
		type.AddInterfaceImplementation(typeof(IUserDefinedFunctionService));
		type.DefineDefaultConstructor(MethodAttributes.Public);
		foreach (var (name, result, parameters) in Legacy) DefineDefaultMethod(type, name, result, parameters);
		var instance = (IUserDefinedFunctionService)Activator.CreateInstance(type.CreateType()!)!;
		await Assert.That(instance.Resolve("missing")).IsNull();
	}

	private static void DefineDefaultMethod(TypeBuilder type, string name, Type result, Type[] parameters)
	{
		var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, result, parameters);
		var il = method.GetILGenerator();
		if (result != typeof(void)) EmitDefault(il, result);
		il.Emit(OpCodes.Ret);
	}

	private static void EmitDefault(ILGenerator il, Type type)
	{
		if (!type.IsValueType) { il.Emit(OpCodes.Ldnull); return; }
		var local = il.DeclareLocal(type);
		il.Emit(OpCodes.Ldloca, local);
		il.Emit(OpCodes.Initobj, type);
		il.Emit(OpCodes.Ldloc, local);
	}
}
