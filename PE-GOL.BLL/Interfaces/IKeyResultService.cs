using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Key Results de un OKR (Spec HU-025 § Lógica BLL).
/// Actor JefeArea (SEC-07, F1); doble compuerta: el OKR padre debe pertenecer al área del JEF
/// y al ciclo activo. El invariante S pesos = 1.000 se aplica SOLO en ActualizarPesosAsync (F0).
/// </summary>
public interface IKeyResultService
{
    /// <summary>GET /api/v1/okrs/{okrId}/key-results — lista los KRs del OKR (CA #1).</summary>
    Task<IEnumerable<KeyResultResponse>> ListarAsync(Guid okrId, CancellationToken ct = default);

    /// <summary>GET /api/v1/okrs/{okrId}/key-results/{id} — detalle de un KR del OKR.</summary>
    Task<KeyResultResponse> ObtenerPorIdAsync(Guid okrId, Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/okrs/{okrId}/key-results — crea un KR con código KR.N (CA #1, CA #2).</summary>
    Task<KeyResultResponse> CrearAsync(Guid okrId, KeyResultCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/okrs/{okrId}/key-results/{id} — edita descripción y peso (CA #1).</summary>
    Task<KeyResultResponse> ActualizarAsync(Guid okrId, Guid id, KeyResultUpdateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/okrs/{okrId}/key-results/pesos — reparto masivo atómico con S = 1.000 (CA #3, F0).</summary>
    Task ActualizarPesosAsync(Guid okrId, KeyResultPesosUpdateRequest request, CancellationToken ct = default);

    /// <summary>DELETE /api/v1/okrs/{okrId}/key-results/{id} — elimina si no tiene valores reales (CA #4).</summary>
    Task EliminarAsync(Guid okrId, Guid id, CancellationToken ct = default);
}
