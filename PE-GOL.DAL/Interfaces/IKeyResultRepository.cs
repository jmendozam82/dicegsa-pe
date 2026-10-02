using System.Data;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Contrato de acceso a datos para Key Results (Spec HU-025 � Queries DAL).
/// Toda query incluye WHERE tenant_id = @TenantId (SEC-06) y AND okr_id = @OkrId (SEC-07 �
/// el okr_id fue validado como perteneciente al �rea del JEF en la BLL), incluidas las tres
/// queries de escritura (ActualizarAsync, ActualizarPesosAsync y EliminarAsync).
/// </summary>
public interface IKeyResultRepository
{
    /// <summary>Query 1 � Lista los KRs del OKR con JOIN okr (OkrCodigo), ORDER BY orden, created_at (CA #1).</summary>
    Task<IEnumerable<KeyResultResponse>> ListarAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>Query 2 � Obtener KR por id dentro del OKR (null si no existe o es de otro OKR � sin fuga).</summary>
    Task<KeyResultResponse?> ObtenerPorIdAsync(Guid tenantId, Guid okrId, Guid id, CancellationToken ct = default);

    /// <summary>Query 3 � Conteo de KRs del OKR (CA #2 / RN-022).</summary>
    Task<ConteoKeyResultsDto> ContarKeyResultsAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>Query 4 � Suma de pesos de los KRs del OKR (CA #3 / RC-06). Uso �nico: verificaci�n
    /// de integridad post-batch dentro de la transacci�n de ActualizarPesosAsync.</summary>
    Task<SumaPesosKeyResultDto> ObtenerSumaPesosAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>Query 5 � Siguiente c�digo KR.N (MAX(regexp_match)+1) y orden por OKR (CA #1, F4).</summary>
    Task<SiguienteSecuenciaKeyResultDto> ObtenerSiguienteSecuenciaKeyResultAsync(Guid tenantId, Guid okrId, CancellationToken ct = default);

    /// <summary>Query 6 � �Tiene el KR valores reales registrados? (CA #4 � EXISTS valor_mensual_kr).</summary>
    Task<bool> VerificarValoresRealesAsync(Guid tenantId, Guid keyResultId, CancellationToken ct = default);

    /// <summary>Query 7 � INSERT KR con las 6 columnas de puntuaci�n a 0.000 (DB-04) ... RETURNING id.</summary>
    Task<Guid> CrearAsync(Guid tenantId, Guid okrId, string codigo, int orden, KeyResultCreateRequest request, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 8 � UPDATE solo descripcion/peso/updated_at (CA #1, DB-04).</summary>
    Task ActualizarAsync(Guid tenantId, Guid okrId, Guid id, KeyResultUpdateRequest request, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 8b � Batch UPDATE de los N pesos del OKR en la transacci�n del caller (F0).
    /// Devuelve Task sin int: el desajuste se detecta en BLL por la relectura de S (paso 6.2).</summary>
    Task ActualizarPesosAsync(Guid tenantId, Guid okrId, List<KeyResultPesoRequest> pesos, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 9 � DELETE f�sico del KR (CA #4).</summary>
    Task EliminarAsync(Guid tenantId, Guid okrId, Guid id, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Query 10 � INSERT log_auditoria (ADR-003).</summary>
    Task<int> InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx = null, CancellationToken ct = default);

    /// <summary>Abre una conexi�n gestionada por el repositorio e inicia una transacci�n IDbTransaction.</summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
