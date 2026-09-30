using System.ComponentModel.DataAnnotations;

namespace ApiInsightStudio.Api.DTOs;

public class ProjectUploadDto
{
    [Required]
    public string OpenApiContent { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }
}
