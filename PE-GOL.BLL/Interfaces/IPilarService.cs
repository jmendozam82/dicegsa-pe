using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Pilares Estratégicos (Spec HU-013 § Lógica BLL — 5 métodos).
/// D-G: escritura GER-only (crear/editar/eliminar — RN-006); GET multi-rol ADM/GER/JEF (CA #4:
/// el JEF lee todos los pilares del ciclo, SEC-07 NO APLICA, D-E). D12: cada método de escritura
/// re-valida el rol desde TenantContext.Rol.
/// D-I: pilar es una entidad con CRUD completo (N filas por ciclo) → servicio dedicado; el DAL
/// vive en ICicloRepository (agregado Ciclo).
/// Lanza AccesoDenegadoException (403), NotFoundException (404) y ValidacionException (422)
/// desde PE_GOL.Utility.Exceptions.
/// Ctor aprobado por spec: (ICicloRepository, TenantContext) + overload (..., ILogger&lt;PilarService&gt;?)
/// — patrón D13 (FilosofiaService HU-011/HU-012).
/// </summary>
public interface IPilarService
{
    /// <summary>GET /api/v1/ciclos/{cicloId}/pilares — 200 con List&lt;PilarResponse&gt; (conteos
    /// CG/OKRs, CA #5; ORDER BY orden ASC, codigo ASC) · 404 (ciclo inexistente/otro tenant/sin tenant).
    /// JefeArea: ve TODOS los pilares (SEC-07 NO APLICA, D-E).</summary>
    Task<List<PilarResponse>> ListarAsync(Guid cicloId, CancellationToken ct = default);

    /// <summary>GET /api/v1/ciclos/{cicloId}/pilares/{pilarId} — 200 con PilarResponse (conteos,
    /// CA #5) · 404 (ciclo/pilar inexistente o de otro tenant).</summary>
    Task<PilarResponse> ObtenerAsync(Guid cicloId, Guid pilarId, CancellationToken ct = default);

    /// <summary>POST /api/v1/ciclos/{cicloId}/pilares — 201 con PilarResponse (código PEC-N
    /// auto-generado, CA #1) · 403 rol ≠ GER (D12) · 404 · 422 (ciclo Cerrado RC-12, nombre vacío/
    /// &gt;150, estrategia &gt;2000, orden negativo, máx 8 pilares CA #2, colisión 23505 ADR-007).</summary>
    Task<PilarResponse> CrearAsync(Guid cicloId, PilarCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/ciclos/{cicloId}/pilares/{pilarId} — 200 con PilarResponse · 403 · 404 ·
    /// 422 (mismas reglas que POST; el código NO es editable, CA #1).</summary>
    Task<PilarResponse> ActualizarAsync(Guid cicloId, Guid pilarId, PilarUpdateRequest request, CancellationToken ct = default);

    /// <summary>DELETE /api/v1/ciclos/{cicloId}/pilares/{pilarId} — 200 con PilarResponse (snapshot
    /// del pilar eliminado, conteos 0) · 403 · 404 · 422 (ciclo Cerrado RC-12, tiene CGs/OKRs CA #3,
    /// FK 23503 capa 2). DELETE físico (D-A).</summary>
    Task<PilarResponse> EliminarAsync(Guid cicloId, Guid pilarId, CancellationToken ct = default);
}