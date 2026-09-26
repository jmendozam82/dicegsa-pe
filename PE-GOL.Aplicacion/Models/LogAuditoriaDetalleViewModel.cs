using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del detalle de una entrada de auditoría (HU-005 § UI).
/// El JSON de valor_anterior/valor_nuevo se renderiza en la vista con el escapado
/// HTML por defecto de Razor (NUNCA @Html.Raw sin sanitización — spec D3/ADR-003).
/// </summary>
public class LogAuditoriaDetalleViewModel
{
    public required LogAuditoriaDetalleResponse Detalle { get; set; }
}