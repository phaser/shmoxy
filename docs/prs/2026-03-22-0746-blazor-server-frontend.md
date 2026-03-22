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

## Verification
- `dotnet build` succeeds with no warnings
- `dotnet test` passes all tests including new theme switching test
- FluentUI components render correctly in both themes
