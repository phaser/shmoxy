# Dependency security rules

- Inspect transitive paths with `dotnet nuget why` before choosing an upgrade.
- Prefer upgrading the direct parent package when it selects a patched
  dependency.
- Add an explicit transitive pin only when the parent still permits a vulnerable
  minimum.
- Verify the entire solution with `dotnet list package --vulnerable
  --include-transitive` after every security upgrade.
