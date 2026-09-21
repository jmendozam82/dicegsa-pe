using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de ciclos (Spec HU-007 § UI — Index.cshtml).
/// Los botones visibles dependen del rol (RN-007): AdminTenant → crear/editar/activar/clonar;
/// Gerente → cerrar; JefeArea → solo lectura.
/// </summary>
public class CiclosIndexViewModel
{
    public List<CicloResponse> Items { get; set; } = [];
    public bool EsAdmin { get; set; }
    public bool EsGerente { get; set; }
}