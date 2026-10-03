using System.Data;
using PE_GOL.DTO.Dtos;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para valores mensuales de KRs (Spec HU-026 § Queries DAL).
/// Toda query incluye WHERE tenant_id = @TenantId (SEC-06) y AND okr_id = @OkrId (SEC-07 —
/// el okr_id fue validado como belonging al área del JEF en la BLL), incluidas UPDATE y DELETE.
/// </summary>
public interface IValorMensualKrRepository
{
    /// <summary>DAL-1 · Lista los valores de TODOS los KRs del OKR (gate okr_id en el JOIN).</summary>
    Task<IEnumerable<ValorMensualKrDto>> ListarPorOkrAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>DAL-2 · Lista los valores de UN KR del OKR (gate okr_id + key_result_id).</summary>
    Task<IEnumerable<ValorMensualKrDto>> ListarPorKrAsync(Guid tenantId, Guid okrId, Guid keyResultId, CancellationToken ct = default);

    /// <summary>DAL-6 · Obtiene el valor de un mes concreto (null si no existe fila).</summary>
    Task<ValorMensualKrDto?> ObtenerValorAsync(Guid tenantId, Guid okrId, Guid keyResultId, int mes, CancellationToken ct = default);

    /// <summary>DAL-3 · UPSERT con ON CONFLICT (key_result_id, mes) DO UPDATE (RC-07 / DB-06).</summary>
    Task<ValorMensualKrDto?> UpsertAsync(Guid tenantId, Guid keyResultId, int mes, decimal valor, Guid usuarioId, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-4 · UPDATE de las 6 columnas de puntuación del KR (DB-04, calculadas en BLL).</summary>
    Task<int> ActualizarPuntuacionesKrAsync(Guid tenantId, Guid okrId, Guid keyResultId, decimal q1, decimal q2, decimal q3, decimal q4, decimal final, decimal ponderada, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-7 · DELETE del valor de un mes (gate okr_id vía USING key_result).</summary>
    Task<int> EliminarAsync(Guid tenantId, Guid okrId, Guid keyResultId, int mes, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>DAL-8 · INSERT log_auditoria (ADR-003).</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexión gestionada por el repositorio e inicia una transacción IDbTransaction.</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
