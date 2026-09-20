namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de registro/edición de Visión y Misión (Spec HU-011 § DTOs — PUT /api/v1/ciclos/{cicloId}/filosofia).
/// Texto enriquecido del CA #1: viaja como HTML sanitizado con allowlist (D-C) — la BLL sanitiza
/// y re-valida (fuente de verdad, UX-04). Máx 5000 caracteres por campo (regla de negocio).
/// valores (JSONB) NO viaja en este request: es responsabilidad de HU-012 (Valores Corporativos).
/// updated_by/updated_at se resuelven en BLL desde el TenantContext (SEC-06) — nunca del body.
/// </summary>
public class FilosofiaUpdateRequest
{
    /// <summary>Requerido, HTML sanitizado, máx 5000 chars (D-C).</summary>
    public string Vision { get; set; } = string.Empty;

    /// <summary>Requerido, HTML sanitizado, máx 5000 chars (D-C).</summary>
    public string Mision { get; set; } = string.Empty;
}