# Dependency security hypotheses

## SQLite transitive pin

The explicit `SQLitePCLRaw.bundle_e_sqlite3` reference can be removed after the
project's selected EF Core SQLite version requires bundle version 2.1.12 or
newer.

Status: unconfirmed. Re-check the resolved graph with `dotnet nuget why` when
upgrading EF Core.
