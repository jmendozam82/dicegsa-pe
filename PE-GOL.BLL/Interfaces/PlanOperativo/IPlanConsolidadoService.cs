using System;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Requests.PlanOperativo;
using PE_GOL.DTO.Responses.PlanOperativo;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de negocio para la vista consolidada del Plan (Spec HU-023 § Lógica BLL).
/// Solo lectura: sin escrituras, sin auditoría, sin transacción.
/// </summary>
public interface IPlanConsolidadoService
{
    /// <summary>
    /// Consulta paginada del consolidado con filtros + resumen de conteos por status.
    /// </summary>
    Task<ConsolidadoResponse> ObtenerConsolidadoAsync(
        FiltrosConsolidadoRequest filtros, CancellationToken ct = default);

    /// <summary>
    /// Exportación a Excel (ClosedXML) con todos los campos. Misma query sin paginación.
    /// </summary>
    Task<byte[]> ExportarConsolidadoAsync(
        FiltrosConsolidadoRequest filtros, CancellationToken ct = default);
}
