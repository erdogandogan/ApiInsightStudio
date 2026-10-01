using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TestController : ControllerBase
{
    private readonly TestGenerationService _testGenerationService;
    private readonly AppDbContext _context;

    /// <summary>Test senaryolarini uretmek icin gerekli servis ve veritabani bagimliliklarini alir.</summary>
    public TestController(TestGenerationService testGenerationService, AppDbContext context)
    {
        _testGenerationService = testGenerationService;
        _context = context;
    }

    /// <summary>Belirtilen proje icin test senaryolarini uretir.</summary>
    [HttpPost("{projectId}/generate")]
    public async Task<IActionResult> GenerateTests([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Gecerli bir kullanici bilgisi bulunamadi." });

        // Proje baskasina aitse "bulunamadi" doner (varligi sizdirilmaz)
        var ownsProject = await _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);
        if (!ownsProject)
            return NotFound(new { message = "Proje bulunamadi." });

        await _testGenerationService.GenerateTestsForProjectAsync(projectId);
        return Ok(new { message = "Test senaryolari basariyla uretildi." });
    }
}
