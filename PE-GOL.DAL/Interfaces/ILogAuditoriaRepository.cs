using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.Saas;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para log_auditoria (Spec HU-005 § Queries DAL: DAL-L1 a DAL-L4).
/// log_auditoria NO está bajo RLS (verificado: no aparece en ENABLE ROW LEVEL SECURITY,
/// 06_MODELO_DATOS.md L465-484) → las queries NO llevan WHERE tenant_id de política; el
/// alcance se garantiza por [Authorize(Roles = "SuperAdmin")] + re-validación BLL (D12).
/// El filtro por tenant_id es un FILTRO DE NEGOCIO opcional (CA #2), no de aislamiento.
/// CA #3 (inmutabilidad): SOLO lectura + EliminarAnterioresAAsync (batch de retención,
/// invocado únicamente por el HostedService — no expuesto por API). Sin Update ni Delete
/// públicos (verificado por el test de contrato #19 de @QA).
/// </summary>
public interface ILogAuditoriaRepository
{
    /// <summary>DAL-L2: COUNT con los mismos filtros dinámicos del listado (paginado).</summary>
    Task<int> CountAsync(LogAuditoriaFiltrosDto filtros, CancellationToken ct = default);

    /// <summary>DAL-L1: SELECT paginado con filtros dinámicos, LEFT JOIN tenant/usuario,
    /// ORDER BY created_at DESC, LIMIT/OFFSET. SIN valor_anterior/valor_nuevo (D3).</summary>
    Task<IEnumerable<LogAuditoriaEntity>> GetPagedAsync(LogAuditoriaFiltrosDto filtros, CancellationToken ct = default);

    /// <summary>DAL-L3: SELECT detalle por id CON valor_anterior/valor_nuevo (JSONB → string).
    /// Retorna null si no existe.</summary>
    Task<LogAuditoriaEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>DAL-L4: DELETE FROM log_auditoria WHERE created_at &lt; @Cutoff (batch de
    /// retención, CA #4). Retorna filas afectadas. ÚNICA operación de borrado sobre la tabla;
    /// solo la invoca LogAuditoriaLimpiezaService (HostedService), nunca la API (CA #3/D12).</summary>
    Task<int> EliminarAnterioresAAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}