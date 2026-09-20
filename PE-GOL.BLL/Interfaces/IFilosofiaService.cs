using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de Visión, Misión y Valores Corporativos del ciclo — Spec HU-011 § Lógica BLL;
/// Spec HU-012 § Lógica BLL (extensión aditiva: ActualizarValoresAsync + mapeo de Valores).
/// La filosofía es hija del agregado Ciclo (D-I): el servicio se inyecta en CicloController
/// (mismo patrón que IAreaService/IResponsableService).
/// SEC-07 NO APLICA (D-E): filosofia es corporativa (una fila por ciclo por tenant, sin area_id;
/// RLS solo por tenant) → el JefeArea lee la filosofía completa del ciclo (RN-007: solo lectura).
/// </summary>
public interface IFilosofiaService
{
    /// <summary>Spec §1 · ObtenerAsync: tenantId (404) → ciclo (404) → DAL-F1 → FilosofiaResponse
    /// (defensivo D-H: Id=Guid.Empty, Vision/Mision="", Valores=[], UpdatedBy/UpdatedAt=null si no
    /// existe fila). HU-012: Valores = DeserializarValores(e.Valores) (parseo JSONB → List&lt;string&gt;).</summary>
    Task<FilosofiaResponse> ObtenerAsync(Guid cicloId, CancellationToken ct = default);

    /// <summary>Spec §2 · ActualizarAsync: rol Gerente (403, D12) → tenantId (404) → ciclo (404)
    /// → RC-12 Cerrado (422) → sanitizar D-C → CA #2 texto visible obligatorio (422) → longitud
    /// ≤ 5000 (422) → snapshot previo (D-D) → tx: DAL-F2 UPSERT + auditoría UPDATE (ADR-003) →
    /// commit → re-lectura → 200. Captura 23505 → 422.</summary>
    Task<FilosofiaResponse> ActualizarAsync(Guid cicloId, FilosofiaUpdateRequest request, CancellationToken ct = default);

    /// <summary>Spec HU-012 §2 · ActualizarValoresAsync: rol Gerente (403, D12) → tenantId (404)
    /// → ciclo (404) → RC-12 Cerrado (422) → trim (D-G) → CA #4 re-validación BLL (min 1, max 15,
    /// no vacío tras trim, ≤100 chars, sin duplicados case-insensitive) → snapshot previo (D-D) →
    /// tx: DAL-F3 UPSERT solo valores + auditoría UPDATE {"valores":[...]} (ADR-003) → commit →
    /// re-lectura → 200. Captura 23505 → 422.</summary>
    Task<FilosofiaResponse> ActualizarValoresAsync(Guid cicloId, ValoresUpdateRequest request, CancellationToken ct = default);
}