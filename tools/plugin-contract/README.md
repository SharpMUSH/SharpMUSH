# Plugin contract check

The plugin contract is the public surface of the `SharpMUSH.Library` package (with the project assemblies it bundles,
such as `SharpMUSH.Contracts` and `SharpMUSH.Configuration`), which C# plugins compile
against. Its version is `PluginContractVersion.Current`, and the loader accepts a plugin built against the same
or an earlier version with the same major. So within a major the contract may grow, but nothing a plugin could
bind to may be removed or changed.

`check.sh` enforces that. CI runs it as the `plugin-contract` job on every pull request.

```bash
tools/plugin-contract/check.sh          # about 30 seconds with a warm build
tools/plugin-contract/check.sh --keep   # keep both packages and validation.log, and print where
```

It packs the package at the commit in `baseline-commit`, packs the working tree's package with the SDK's package
validation against it, and fails on any breaking change (`CP0002` member removed, `CP0006` interface member
added, and the rest of the `CP` rules). It also fails when the `<Version>` of `SharpMUSH.Library` or
`SharpMUSH.Implementation.Generated` has a different major from `PluginContractVersion.Current`.

## When it fails

Either keep the old member beside the new one (an overload, or the old record constructor), or make it a major
change: bump `PluginContractVersion.Current` to the next major and both package versions with it. A plugin built
against the old major is then refused at load with a clear reason instead of failing on a missing method.

Adding a member to a public interface counts as a break, because a plugin may implement that interface.

A break cannot be suppressed: the check fails when `SharpMUSH.Library.csproj` or `Directory.Build.*` names a `CP` or
`PKV` code, or when the project holds a compatibility suppression file.

Additions are not checked. A plugin that uses a member added after 3.0.0 still loads on a 3.0.0 server and fails
there when it calls it, so a release that adds to the contract bumps the minor, and such a plugin declares that
minor (`>=3.1`).

## The baseline

`baseline-commit` holds the commit whose package is the frozen surface; its contract version is read from that
commit's `PluginContractVersion.cs`. While `PluginContractVersion.Current` has a newer major than the baseline,
the check passes with a notice. Once the release that ships the new major is tagged, point `baseline-commit` at
it.

The baseline is packed with its own `global.json`. When the SDK moves to a new major, move `baseline-commit` to a
commit that builds on it.

Moving `baseline-commit` within the same major accepts every break since the old commit, so a pull request that
changes it says why.
