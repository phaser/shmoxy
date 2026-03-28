# Theme Customization

The frontend uses Microsoft Fluent UI Blazor (v4.14.0). Themes are controlled via the `<FluentDesignTheme>` component in `MainLayout.razor`.

## Available Modes

`DesignThemeModes`: `Light`, `Dark`, `System` (follows OS preference)

## Color Palettes

### Preset Office Colors (`OfficeColor` property)

| Name | Color |
|------|-------|
| Default | Fluent blue |
| Teams | Purple |
| Word | Blue |
| Excel | Green |
| Outlook | Blue |
| PowerPoint | Red/orange |
| OneNote | Purple |
| SharePoint | Teal |

### Custom Color (`CustomColor` property)

Any hex color string. Fluent UI generates the full neutral + accent ramp from it.

## Current Setup

```razor
<FluentDesignTheme @ref="_theme" @bind-Mode="_mode" StorageName="preferred-theme" />
```

## To Add a Palette

Add `OfficeColor` for a preset or `CustomColor` for a custom accent:

```razor
<FluentDesignTheme @ref="_theme" @bind-Mode="_mode" OfficeColor="OfficeColor.Teams" StorageName="preferred-theme" />
<!-- or -->
<FluentDesignTheme @ref="_theme" @bind-Mode="_mode" CustomColor="#7B3FF2" StorageName="preferred-theme" />
```

For per-mode customization (e.g. different dark background), override Fluent CSS variables in `wwwroot/css/app.css`:

```css
.fluent-dark {
    --neutral-layer-1: #1a1a2e;
    --neutral-layer-2: #16213e;
}
```

## TODO

- Add theme palette picker to Settings page (dropdown for OfficeColor presets + custom hex input)
- Consider separate accent colors for light vs dark mode
