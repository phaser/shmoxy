using Microsoft.AspNetCore.Mvc;
using shmoxy.api.data;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ProxiesDbContext _db;

    public SettingsController(ProxiesDbContext db)
    {
        _db = db;
    }

    [HttpGet("retention")]
    public async Task<ActionResult<RetentionPolicyDto>> GetRetentionPolicy(CancellationToken cancellationToken)
    {
        var policy = await SessionRetentionService.LoadRetentionPolicyAsync(_db, cancellationToken);
        return Ok(policy);
    }

    [HttpPut("retention")]
    public async Task<ActionResult<RetentionPolicyDto>> UpdateRetentionPolicy(
        [FromBody] RetentionPolicyDto policy, CancellationToken cancellationToken)
    {
        await SessionRetentionService.SaveRetentionPolicyAsync(_db, policy, cancellationToken);
        return Ok(policy);
    }
}
