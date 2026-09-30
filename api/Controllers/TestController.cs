using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TestController : ControllerBase
{
    private readonly TestGenerationService _testGenerationService;

    /// <summary>Test senaryolarini uretmek icin gerekli servis bagimliligini alir.</summary>
    public TestController(TestGenerationService testGenerationService)
    {
        _testGenerationService = testGenerationService;
    }

    /// <summary>Belirtilen proje icin test senaryolarini uretir.</summary>
    [HttpPost("{projectId}/generate")]
    public async Task<IActionResult> GenerateTests([FromRoute] int projectId)
    {
        await _testGenerationService.GenerateTestsForProjectAsync(projectId);
        return Ok(new { message = "Test senaryolari basariyla uretildi." });
    }
}
