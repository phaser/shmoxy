# Blazor Server Frontend - Implementation Plan

## Goals
- Use Blazor Server served from shmoxy-api for simplicity
- Frontend sources in separate project for clear separation
- All Blazor configuration lives in shmoxy.frontend project
- Fluent Design System with dark/light mode support
- Playwright integration tests in tests/shmoxy.frontend.tests

## Plan

### Phase 1: Project Structure
1. Create `src/shmoxy.frontend/shmoxy.frontend.csproj`
   - Blazor Server project targeting net10.0
   - Reference Microsoft.FluentUI.AspNetCore.Components (v4.14.0+)
   - Reference Microsoft.FluentUI.AspNetCore.Components.Icons (optional)
   - Reference shmoxy.api for shared models/configuration

2. Update `src/shmoxy.slnx`
   - Add shmoxy.frontend project reference
   - Add tests/shmoxy.frontend.tests project reference

3. Create `tests/shmoxy.frontend.tests/shmoxy.frontend.tests.csproj`
   - xUnit test project with Playwright
   - Reference shmoxy.api (test host)
   - Reference shmoxy.frontend (SUT)

### Phase 2: Blazor Server Configuration (in shmoxy.frontend)
4. Create extension method class `FluentUiBlazorConfiguration` in shmoxy.frontend
   - `AddBlazorFrontend(this IServiceCollection services)` - registers Razor pages, Blazor server, FluentUI components
   - `MapBlazorFrontend(this WebApplication app)` - maps Blazor hub, static files, fallback page

5. Update `shmoxy.api/Program.cs`
   - Call `builder.Services.AddBlazorFrontend()` from shmoxy.frontend
   - Call `app.MapBlazorFrontend()` after MapControllers()

### Phase 3: UI Implementation
6. Create base layout (`_Layout.razor`)
   - FluentUI theme provider with dark/light mode support
   - Use design tokens for theme switching
   - Include FluentToastProvider, FluentDialogProvider, etc.

7. Create main pages:
   - `Pages/Home.razor` - Dashboard/landing page
   - `Pages/ProxyConfig.razor` - Proxy configuration UI
   - `Pages/Inspection.razor` - Request inspection viewer

8. Implement theme toggle component
   - Button/switch to toggle dark/light mode
   - Persist preference in browser localStorage
   - Use FluentUI design tokens for theme application

### Phase 4: Integration
9. Create API client service in shmoxy.frontend
   - HttpClient wrapper for shmoxy.api endpoints
   - Strongly-typed methods for proxy config, inspection, etc.

10. Wire up pages to API
    - ProxyConfig page → ConfigController
    - Inspection page → InspectionController
    - Display proxy status from ProxiesController

### Phase 5: Testing
11. Create Playwright test fixture for frontend tests
    - Reuse existing ProxyTestFixture pattern
    - Start shmoxy.api with Blazor frontend enabled
    - Launch browser with tracing support

12. Implement theme switching test
    - Navigate to frontend
    - Verify default theme (dark or light)
    - Click theme toggle
    - Assert theme changed (check CSS custom properties or data attributes)
    - Toggle back and verify

13. Run full test suite: `dotnet test`

## Technical Decisions

### Fluent UI Library
- **Package**: `Microsoft.FluentUI.AspNetCore.Components` (v4.14.0, Feb 2026)
- **Why**: Official Microsoft Fluent UI Blazor components, 110+ components, built-in theme support
- **Alternatives considered**:
  - Radzen.Blazor (Material-focused, less Fluent)
  - BlazorFluentUI (older, less maintained)
  - Manual implementation (too much work)

### Theme Implementation
- Use FluentUI design tokens for theme switching
- Store user preference in localStorage
- CSS custom properties automatically updated by FluentUI components

### Project Organization
- Configuration extension methods live in shmoxy.frontend
- shmoxy.api only calls into shmoxy.frontend, no Blazor knowledge needed in API
- Clean separation of concerns

## Files to Create
- `src/shmoxy.frontend/shmoxy.frontend.csproj`
- `src/shmoxy.frontend/_Imports.razor`
- `src/shmoxy.frontend/App.razor`
- `src/shmoxy.frontend/Routes.razor`
- `src/shmoxy.frontend/Layout/MainLayout.razor`
- `src/shmoxy.frontend/Pages/Home.razor`
- `src/shmoxy.frontend/Services/ThemeService.cs`
- `src/shmoxy.frontend/Services/ApiClient.cs`
- `src/shmoxy.frontend/Extensions/FluentUiBlazorConfiguration.cs`
- `tests/shmoxy.frontend.tests/shmoxy.frontend.tests.csproj`
- `tests/shmoxy.frontend.tests/FrontendTestFixture.cs`
- `tests/shmoxy.frontend.tests/ThemeSwitchingTests.cs`

## Files to Modify
- `src/shmoxy.slnx` - add project references
- `src/shmoxy.api/shmoxy.api.csproj` - add reference to shmoxy.frontend
- `src/shmoxy.api/Program.cs` - call AddBlazorFrontend() and MapBlazorFrontend()

## Running the Application

```bash
cd src/shmoxy.api
dotnet run
```

The API starts on `https://localhost:5001` (or `http://localhost:5000`) by default. The Blazor frontend is served from the same host — navigate to the root URL in your browser to access the UI.

### Pages

- `/` — Dashboard with links to proxy config and inspection
- `/proxy-config` — Configure proxy host, port, HTTPS interception
- `/inspection` — View intercepted requests with method/status filtering

### Theme

Dark mode is the default. Click the sun/moon toggle in the header to switch. The preference is persisted in browser localStorage.

## Implementation Status (as of 2026-03-22)

### Phase 1: Project Structure - COMPLETE
- [x] Created `src/shmoxy.frontend/shmoxy.frontend.csproj` as Razor Class Library (Microsoft.NET.Sdk.Razor)
- [x] Updated `src/shmoxy.slnx` with project references
- [x] Created `tests/shmoxy.frontend.tests/shmoxy.frontend.tests.csproj`

### Phase 2: Blazor Server Configuration - COMPLETE
- [x] Created `extensions/FluentUiBlazorConfiguration.cs` with `AddBlazorFrontend()` / `MapBlazorFrontend()` extension methods
- [x] Uses .NET 8+ APIs: `AddRazorComponents().AddInteractiveServerComponents()` / `MapRazorComponents<App>()`
- [x] Updated `shmoxy.api/Program.cs` to call extension methods

### Phase 3: UI Implementation - COMPLETE
- [x] Created `App.razor` root component (full HTML document host with `RenderMode.InteractiveServer`)
- [x] Created `Routes.razor` with Router and fallback
- [x] Created `layout/MainLayout.razor` with FluentUI nav menu, header, and theme toggle
- [x] Created `pages/Home.razor` — Dashboard with FluentCard links
- [x] Created `pages/ProxyConfig.razor` — Config form with FluentTextField, FluentNumberField, FluentSwitch
- [x] Created `pages/Inspection.razor` — Request grid with FluentDataGrid, FluentSelect filters
- [x] Theme persistence via localStorage with JS interop (`wwwroot/js/app.js`)

### Phase 4: Integration - COMPLETE
- [x] Created `services/ThemeService.cs` for theme management
- [x] Created `services/ApiClient.cs` for API communication
- [x] Model types split into separate files under `models/`
- [x] Configured DI container with scoped ApiClient

### Phase 5: Testing Infrastructure - COMPLETE
- [x] Created `FrontendTestFixture.cs` with WebApplicationFactory + Playwright
- [x] Created `ThemeSwitchingTests.cs` with theme toggle, navigation, and dashboard card tests

### Phase 6: Bug Fixes and Cleanup - COMPLETE
- [x] Fixed project SDK: changed from `Microsoft.NET.Sdk.Web` to `Microsoft.NET.Sdk.Razor`
- [x] Fixed .NET 8+ Blazor Server APIs (replaced `AddServerSideBlazor`/`MapBlazorHub` with `MapRazorComponents`)
- [x] Fixed all FluentUI v4 component usage (Icon, Card, Button, DataGrid, Select, etc.)
- [x] Fixed `@bind-Value` syntax (was `Bind-Value`)
- [x] Removed invalid `IJSRuntime.Dispose()` from ThemeService
- [x] Changed `FrontendProxyConfig` from record to class (mutable props for two-way binding)
- [x] Removed unused NavMenu component
- [x] Removed unnecessary `wwwroot/index.html` (App.razor is the host)
- [x] Fixed pre-existing warnings in `shmoxy.api.tests` (null dereferences, unused field, blocking `.Result`)
- [x] Renamed all frontend directories to lowercase
- [x] Added `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` to shmoxy.api.csproj (required because the host has no .razor files — without this, `_framework/blazor.web.js` is missing from static assets and Blazor interactivity doesn't work)
- [x] Added `app.MapStaticAssets()` for .NET 10 static asset serving (required to serve `_framework/blazor.web.js`)
- [x] Test fixture sets `ASPNETCORE_APPLICATIONNAME=shmoxy.api` so static web assets manifest is found from test bin directory
- [x] Added `launchSettings.json` for Development environment in IDE

### Files Created
- `src/shmoxy.frontend/App.razor` — Root component
- `src/shmoxy.frontend/Routes.razor` — Router
- `src/shmoxy.frontend/_Imports.razor` — Global usings
- `src/shmoxy.frontend/extensions/FluentUiBlazorConfiguration.cs`
- `src/shmoxy.frontend/layout/MainLayout.razor`
- `src/shmoxy.frontend/pages/Home.razor`
- `src/shmoxy.frontend/pages/ProxyConfig.razor`
- `src/shmoxy.frontend/pages/Inspection.razor`
- `src/shmoxy.frontend/services/ApiClient.cs`
- `src/shmoxy.frontend/services/ThemeService.cs`
- `src/shmoxy.frontend/models/FrontendProxyConfig.cs`
- `src/shmoxy.frontend/models/FrontendProxyStatus.cs`
- `src/shmoxy.frontend/models/ProxyInfo.cs`
- `src/shmoxy.frontend/models/InspectionRequestInfo.cs`
- `src/shmoxy.frontend/wwwroot/css/app.css`
- `src/shmoxy.frontend/wwwroot/js/app.js`
- `tests/shmoxy.frontend.tests/FrontendTestFixture.cs`
- `tests/shmoxy.frontend.tests/ThemeSwitchingTests.cs`

### Files Modified
- `src/shmoxy.slnx` — Added shmoxy.frontend and test project references
- `src/shmoxy.api/Program.cs` — Calls `AddBlazorFrontend()` / `MapBlazorFrontend()`
- `src/shmoxy.api/shmoxy.api.csproj` — Added project reference to shmoxy.frontend
- `tests/shmoxy.api.tests/` — Fixed pre-existing compiler warnings

## Verification
- [x] `dotnet build` succeeds with zero warnings and zero errors
- [x] `dotnet test` — all 105 tests pass (10 shmoxy.tests + 63 shmoxy.api.tests + 4 shmoxy.frontend.tests + 28 shmoxy.e2e)
- [x] Manual: start API, navigate to UI, verify pages render with FluentUI styling
- [x] Manual: theme toggle works (verified via Playwright test clicking button and checking localStorage)
