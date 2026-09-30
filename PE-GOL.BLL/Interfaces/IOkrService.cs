using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de OKRs del área (Spec HU-024 § Lógica BLL).
/// Actor JefeArea (SEC-07); campos calculados iniciales 0.000/'Rojo' (DB-04, F3).
/// </summary>
public interface IOkrService
{
    /// <summary>GET /api/v1/okrs — lista los OKRs del área en el ciclo activo (CA #4).</summary>
    Task<IEnumerable<OkrResponse>> ListarAsync(CancellationToken ct = default);

    /// <summary>GET /api/v1/okrs/{id} — detalle de un OKR del área.</summary>
    Task<OkrResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/v1/okrs — crea un OKR del área con código OKR.N (CA #1).</summary>
    Task<OkrResponse> CrearAsync(OkrCreateRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/v1/okrs/{id} — edita descripción y pilar (CA #2).</summary>
    Task<OkrResponse> ActualizarAsync(Guid id, OkrUpdateRequest request, CancellationToken ct = default);

    /// <summary>DELETE /api/v1/okrs/{id} — elimina físicamente si no tiene KRs con valores reales (CA #3).</summary>
    Task EliminarAsync(Guid id, CancellationToken ct = default);
}
