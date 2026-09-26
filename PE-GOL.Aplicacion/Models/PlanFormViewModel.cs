namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario create/edit de plan (HU-002 § UI).
/// Sin estado: el DDL de plan no tiene columna de estado (D2/D3 del spec).
/// Los rangos (maxAreas 1..20, maxUsuarios 1..1000, maxCiclosActivos 1..10)
/// se validan en el cliente (jQuery Validate) y en la API/BLL (fuente de verdad).
/// </summary>
public class PlanFormViewModel
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int MaxAreas { get; set; } = 10;
    public int MaxUsuarios { get; set; } = 25;
    public int MaxCiclosActivos { get; set; } = 1;
}