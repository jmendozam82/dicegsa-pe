namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de registro/edición de Valores Corporativos (Spec HU-012 § DTOs —
/// PUT /api/v1/ciclos/{cicloId}/filosofia/valores).
/// Lista YA ordenada (D-C): el orden del array JSONB ES el orden de visualización
/// (drag-and-drop/flechas = reordenar el array; el frontend de HU-045+ envía la lista final).
/// CA #4: mínimo 1 valor, máximo 15; cada valor: trim, no vacío, máx 100 chars; sin duplicados
/// case-insensitive (re-validación BLL = fuente de verdad, UX-04).
/// updated_by/updated_at se resuelven en BLL desde el TenantContext (SEC-06) — nunca del body.
/// </summary>
public class ValoresUpdateRequest
{
    /// <summary>Lista YA ordenada de valores corporativos (D-C).</summary>
    public List<string> Valores { get; set; } = new();
}