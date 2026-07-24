# Dependency security knowledge

## Confirmed facts

- `Swashbuckle.AspNetCore` 10.1.7 resolved vulnerable `Microsoft.OpenApi` 2.4.1.
  Upgrading Swashbuckle to 10.2.3 resolves patched `Microsoft.OpenApi` 2.7.5.
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.0 permits
  `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11. An explicit bundle reference at
  2.1.12 upgrades the native `SQLitePCLRaw.lib.e_sqlite3` dependency to 2.1.12.
- `dotnet list src/shmoxy.slnx package --vulnerable --include-transitive`
  reports no vulnerable packages after those changes.
