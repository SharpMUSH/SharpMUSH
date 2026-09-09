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
		var instance = CreateLegacyRegistry();
		await Assert.That(instance.Resolve("missing")).IsNull();
	}

	[Test]
	public async Task LegacyRegistryInvalidationIsHarmless()
	{
		var instance = CreateLegacyRegistry();
		instance.InvalidateLocalDefinitions(new DBRef(10));
		instance.InvalidateLocalName("name");
		await Assert.That(instance.Resolve("missing")).IsNull();
	}

	internal static IUserDefinedFunctionService CreateLegacyRegistry(bool supportsScopedGet = false)
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LegacyRegistry" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
		var type = assembly.DefineDynamicModule("legacy").DefineType("LegacyImplementation", TypeAttributes.Public);
		type.AddInterfaceImplementation(typeof(IUserDefinedFunctionService));
		type.DefineDefaultConstructor(MethodAttributes.Public);
		var calls = type.DefineField("DefinitionCalls", typeof(int), FieldAttributes.Public);
		foreach (var (name, result, parameters) in Legacy)
		{
			if (name != "Define") { DefineDefaultMethod(type, name, result, parameters); continue; }
			var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, result, parameters);
			var il = method.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, calls); il.Emit(OpCodes.Ret);
		}
		if (supportsScopedGet) DefineDefaultMethod(type, "Get", typeof(UserDefinedFunction), [typeof(string), typeof(DBRef?)]);
		return (IUserDefinedFunctionService)Activator.CreateInstance(type.CreateType()!)!;
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
