using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario create/edit de tenant (Spec HU-045 § Vistas Razor / ViewModels).
/// Estado NO es editable (D-1): solo se muestra como badge en Edit; crear siempre genera 'Activo'.
/// </summary>
public class TenantFormViewModel
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid PlanId { get; set; }
    public string? LogoUrl { get; set; }
    public string? Eslogan { get; set; }
    public string? ZonaHoraria { get; set; }
    public string Estado { get; set; } = string.Empty;
    public List<PlanResponse> Planes { get; set; } = [];
}