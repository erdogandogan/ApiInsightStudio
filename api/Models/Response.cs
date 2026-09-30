using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class Response
{
    /// <summary>Yanıt kaydının benzersiz kimliğidir.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Yanıtın ait olduğu endpoint kimliğidir.</summary>
    [ForeignKey(nameof(Endpoint))]
    public int EndpointId { get; set; }

    /// <summary>HTTP durum kodudur (örn: 200, 404).</summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary>Yanıt açıklamasıdır.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Yanıtın ait olduğu endpoint bilgisidir.</summary>
    public Endpoint Endpoint { get; set; } = null!;
}
