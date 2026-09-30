using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ApiInsightStudio.Api.Models;

public class User
{
    /// <summary>Kullanıcı kimliği.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Kullanıcı adı.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kullanıcı e-posta adresi.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Şifre özeti (hash) değeri.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Kayıt oluşturulma tarihi (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Kullanıcının sahip olduğu projeler.</summary>
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
