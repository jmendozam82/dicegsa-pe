namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de la configuración de la empresa (Spec HU-006 § UI — vista Configuracion.cshtml).
/// EsAdmin decide el modo: AdminTenant → formulario editable; Gerente/JefeArea → solo lectura (RN-007).
/// Zonas: catálogo IANA (ZonasIANA, America/* primero) para el select de zona horaria.
/// </summary>
public class EmpresaViewModel
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Eslogan { get; set; }

    /// <summary>URL firmada (24 h) generada en cada lectura; null si no hay logo.</summary>
    public string? LogoUrl { get; set; }

    public string ZonaHoraria { get; set; } = "America/Managua";

    /// <summary>Solo lectura (gestionada por SuperAdmin en HU-001).</summary>
    public string? Descripcion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>true → formulario editable (AdminTenant); false → solo lectura (Gerente/JefeArea).</summary>
    public bool EsAdmin { get; set; }

    /// <summary>Catálogo IANA ordenado (America/* primero) para el select.</summary>
    public List<string> Zonas { get; set; } = [];
}