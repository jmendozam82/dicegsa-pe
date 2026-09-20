using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.DTO.Common;

namespace PE_GOL.BLL.Interfaces;

public interface IObjetivoCgService
{
    Task<IEnumerable<ObjetivoCgResponse>> ListarAsync();
    Task<ObjetivoCgResponse> ObtenerPorIdAsync(Guid id);
    Task<ObjetivoCgResponse> CrearAsync(ObjetivoCgCreateRequest request);
    Task<ObjetivoCgResponse> ActualizarAsync(Guid id, ObjetivoCgUpdateRequest request);
    Task EliminarAsync(Guid id);
    Task<ApiResponse<IEnumerable<ObjetivoCgConsolidadoResponse>>> ListarConsolidadoGerenteAsync(ObjetivoCgFilterRequest filtros, CancellationToken ct = default);
}
