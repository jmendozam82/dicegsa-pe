using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.BLL.Interfaces;

public interface IObjetivoCgService
{
    Task<IEnumerable<ObjetivoCgResponse>> ListarAsync();
    Task<ObjetivoCgResponse> ObtenerPorIdAsync(Guid id);
    Task<ObjetivoCgResponse> CrearAsync(ObjetivoCgCreateRequest request);
    Task<ObjetivoCgResponse> ActualizarAsync(Guid id, ObjetivoCgUpdateRequest request);
    Task EliminarAsync(Guid id);
    /// <summary>GET /api/v1/objetivos-cg/consolidado — Vista consolidada para el Gerente.
    /// ARCH-02: retorna el DTO directamente; el controller construye ApiResponse<T> (ARCH-07).</summary>
    Task<IEnumerable<ObjetivoCgConsolidadoResponse>> ListarConsolidadoGerenteAsync(ObjetivoCgFilterRequest filtros, CancellationToken ct = default);
}
