using System.Data;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.DAL.Interfaces;

public interface IObjetivoCgRepository
{
    Task<IEnumerable<ObjetivoCgResponse>> ListarAsync(Guid tenantId, Guid cicloId, Guid areaId);
    Task<ObjetivoCgResponse?> ObtenerPorIdAsync(Guid tenantId, Guid areaId, Guid id);
    Task<ObjetivoCgResponse?> ObtenerPorIdSinAreaAsync(Guid tenantId, Guid id);
    Task<Guid> CrearAsync(Guid tenantId, Guid cicloId, Guid areaId, string codigo, ObjetivoCgCreateRequest request, IDbTransaction? tx = null);
    Task ActualizarAsync(Guid id, ObjetivoCgUpdateRequest request, IDbTransaction? tx = null);
    Task EliminarAsync(Guid id, IDbTransaction? tx = null);
    Task<bool> VerificarAccionesAsociadasAsync(Guid tenantId, Guid objetivoCgId);
    Task<int> ObtenerConteoPorAreaAsync(Guid tenantId, Guid cicloId, Guid areaId);
    Task<string?> ObtenerCodigoAreaAsync(Guid tenantId, Guid areaId);

    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task<int> InsertLogAsync(PE_GOL.DTO.Dtos.LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<IEnumerable<ObjetivoCgConsolidadoResponse>> ListarConsolidadoGerenteAsync(Guid tenantId, Guid cicloId, ObjetivoCgFilterRequest filtros, CancellationToken ct = default);
    /// <summary>Recalcula progreso y semáforo del objetivo_cg a partir del promedio ponderado de sus acciones (RN-018, HU-020).</summary>
    Task RecalcularProgresoAsync(Guid objetivoCgId, decimal nuevoPorcentaje, string nuevoSemaforo, Guid tenantId, CancellationToken ct = default);
}
