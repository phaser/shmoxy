using Microsoft.FluentUI.AspNetCore.Components;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class ThemeStateTests
{
    [Fact]
    public void DefaultMode_IsDark()
    {
        var state = new ThemeState();
        Assert.Equal(DesignThemeModes.Dark, state.Mode);
    }

    [Fact]
    public void SetMode_UpdatesMode()
    {
        var state = new ThemeState();
        state.SetMode(DesignThemeModes.Light);
        Assert.Equal(DesignThemeModes.Light, state.Mode);
    }

    [Fact]
    public void SetMode_RaisesOnChange()
    {
        var state = new ThemeState();
        var raised = false;
        state.OnChange += () => raised = true;

        state.SetMode(DesignThemeModes.Light);

        Assert.True(raised);
    }

    [Fact]
    public void Toggle_SwitchesDarkToLight()
    {
        var state = new ThemeState();
        Assert.Equal(DesignThemeModes.Dark, state.Mode);

        state.Toggle();

        Assert.Equal(DesignThemeModes.Light, state.Mode);
    }

    [Fact]
    public void Toggle_SwitchesLightToDark()
    {
        var state = new ThemeState();
        state.SetMode(DesignThemeModes.Light);

        state.Toggle();

        Assert.Equal(DesignThemeModes.Dark, state.Mode);
    }

    [Fact]
    public void Toggle_RaisesOnChange()
    {
        var state = new ThemeState();
        var count = 0;
        state.OnChange += () => count++;

        state.Toggle();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Toggle_TwiceReturnsToOriginal()
    {
        var state = new ThemeState();
        state.Toggle();
        state.Toggle();
        Assert.Equal(DesignThemeModes.Dark, state.Mode);
    }
}
