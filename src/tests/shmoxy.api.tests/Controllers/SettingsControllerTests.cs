using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using shmoxy.api.Controllers;
using shmoxy.api.data;
using shmoxy.api.models.dto;

namespace shmoxy.api.tests.Controllers;

public class SettingsControllerTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;
    private readonly SettingsController _controller;

    public SettingsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _dbContext.Database.EnsureCreated();
        _controller = new SettingsController(_dbContext);
    }

    [Fact]
    public async Task GetRetentionPolicy_ReturnsDefaultPolicy_WhenNoneExists()
    {
        var result = await _controller.GetRetentionPolicy(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var policy = Assert.IsType<RetentionPolicyDto>(okResult.Value);
        Assert.False(policy.Enabled);
        Assert.Null(policy.MaxAgeDays);
        Assert.Null(policy.MaxCount);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_ReturnsOk()
    {
        var policy = new RetentionPolicyDto
        {
            Enabled = true,
            MaxAgeDays = 30,
            MaxCount = 100
        };

        var result = await _controller.UpdateRetentionPolicy(policy, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsType<RetentionPolicyDto>(okResult.Value);
        Assert.True(returned.Enabled);
        Assert.Equal(30, returned.MaxAgeDays);
        Assert.Equal(100, returned.MaxCount);
    }

    [Fact]
    public async Task UpdateThenGet_ReturnsSavedPolicy()
    {
        var policy = new RetentionPolicyDto
        {
            Enabled = true,
            MaxAgeDays = 7,
            MaxCount = 50
        };

        await _controller.UpdateRetentionPolicy(policy, CancellationToken.None);
        var result = await _controller.GetRetentionPolicy(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsType<RetentionPolicyDto>(okResult.Value);
        Assert.True(returned.Enabled);
        Assert.Equal(7, returned.MaxAgeDays);
        Assert.Equal(50, returned.MaxCount);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_DisabledPolicy_PersistsCorrectly()
    {
        var policy = new RetentionPolicyDto
        {
            Enabled = false,
            MaxAgeDays = null,
            MaxCount = null
        };

        var result = await _controller.UpdateRetentionPolicy(policy, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsType<RetentionPolicyDto>(okResult.Value);
        Assert.False(returned.Enabled);
        Assert.Null(returned.MaxAgeDays);
        Assert.Null(returned.MaxCount);

        // Verify disabled policy persists via a fresh GET
        var getResult = await _controller.GetRetentionPolicy(CancellationToken.None);
        var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
        var persisted = Assert.IsType<RetentionPolicyDto>(getOk.Value);
        Assert.False(persisted.Enabled);
    }

    [Fact]
    public async Task UpdateRetentionPolicy_OverwritesPreviousValues()
    {
        // Save initial policy
        var initial = new RetentionPolicyDto { Enabled = true, MaxAgeDays = 30, MaxCount = 100 };
        await _controller.UpdateRetentionPolicy(initial, CancellationToken.None);

        // Overwrite with different values
        var updated = new RetentionPolicyDto { Enabled = true, MaxAgeDays = 7, MaxCount = 50 };
        await _controller.UpdateRetentionPolicy(updated, CancellationToken.None);

        // Verify the latest values are returned
        var result = await _controller.GetRetentionPolicy(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var policy = Assert.IsType<RetentionPolicyDto>(okResult.Value);
        Assert.Equal(7, policy.MaxAgeDays);
        Assert.Equal(50, policy.MaxCount);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
