# SharpMUSH command and function generators

Roslyn source generators for C# SharpMUSH plugins. This package generates
`CommandLibrary` and `FunctionLibrary` entries from methods marked with
`[SharpCommand]` and `[SharpFunction]`.

Reference this package alongside `SharpMUSH.Library`, using matching versions:

```xml
<PackageReference Include="SharpMUSH.Implementation.Generated" Version="3.0.0"
                  PrivateAssets="all" />
```

The generator assembly ships under `analyzers/dotnet/cs`; it is needed at build
time. See the [plugin author guide](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/guides/writing-a-plugin.md)
for a complete example and the required SDK.
