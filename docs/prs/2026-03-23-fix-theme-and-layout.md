# PR: Fix theme switching and redesign layout

**Created:** 2026-03-23
**Branch:** fix/fix-theme

## Description

Fixes the broken theme switching (dark/light mode had no visual effect) and redesigns the frontend layout to match the Aspire Dashboard style — narrow icon sidebar, top header bar, and a dedicated Settings page for theme control.

## Status

- [x] Development in progress
- [x] Tests added/updated
- [x] Documentation updated
- [x] Ready for review
- [ ] Merged

## Changes Made

### Theme fix
- **Root cause**: The old implementation used custom JS (`applyTheme`) that set `data-theme` attribute and body classes, but nothing in CSS or FluentUI responded to those attributes. The page background never changed.
- **Fix**: Replaced manual JS interop with `<FluentDesignTheme>` component (`@bind-Mode` + `StorageName`), which controls FluentUI design tokens (`--base-layer-luminance`) and actually changes the visual theme.
- Removed dead code: `ThemeService.cs` (unused service), custom JS functions in `app.js` (`applyTheme`, `setLocalStorage`, `getLocalStorage`, `matchMediaQuery`).

### Layout redesign (Aspire Dashboard style)
- **Header**: 48px top bar with app icon + "Shmoxy" title on left, settings gear icon on right.
- **Icon sidebar**: 60px narrow sidebar with vertically stacked icon+label nav items (Home, Proxy, Inspection, Settings). Replaces the previous 220px `FluentNavMenu`.
- **Content area**: Fills remaining space with padding.

### New Settings page
- Added `/settings` page with a `FluentSwitch` to toggle dark/light theme.
- Created `ThemeState` scoped service to share theme mode between `MainLayout` and `Settings` page.
- Registered `ThemeState` in DI via `FluentUiBlazorConfiguration`.

### Test updates
- `ThemeToggle_ChangesTheme`: Navigates to `/settings`, clicks the switch, verifies `--base-layer-luminance` CSS variable actually changes (not just localStorage), then toggles back and verifies revert.
- `NavigationSidebar_HasExpectedLinks`: Updated selector for new icon sidebar, expects 4 links.
- All existing tests (`PageLoads_WithTitle`, `DashboardPage_LoadsCards`) still pass.

## Files Created
- `src/shmoxy.frontend/pages/Settings.razor` — Settings page with theme switch
- `src/shmoxy.frontend/services/ThemeState.cs` — Shared theme state service

## Files Modified
- `src/shmoxy.frontend/layout/MainLayout.razor` — Aspire-style layout with icon sidebar
- `src/shmoxy.frontend/wwwroot/css/app.css` — Complete rewrite for new layout
- `src/shmoxy.frontend/extensions/FluentUiBlazorConfiguration.cs` — Register ThemeState
- `src/shmoxy.frontend/_Imports.razor` — Added DesignTokens using
- `src/shmoxy.frontend/wwwroot/js/app.js` — Cleared unused JS functions
- `src/tests/shmoxy.frontend.tests/ThemeSwitchingTests.cs` — Updated for new layout/settings page

## Files Deleted
- `src/shmoxy.frontend/services/ThemeService.cs` — Dead code, was never used

## Testing

- All 4 frontend Playwright tests pass
- Theme toggle test verifies actual visual change via `--base-layer-luminance` CSS custom property
- Build succeeds with 0 warnings, 0 errors

## Notes

- Theme defaults to dark mode. `FluentDesignTheme` with `StorageName="preferred-theme"` persists the choice in localStorage.
- The `fluent-card { overflow: visible }` CSS rule ensures FluentUI popups aren't clipped by card boundaries.
