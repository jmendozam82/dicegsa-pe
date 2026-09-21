namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel de configuración de umbrales de semáforo (Spec HU-008 § UI — Umbrales.cshtml).
/// EsEditable = AdminTenant && ciclo en 'Borrador' (RN-039/RC-12): solo entonces se muestra el
/// formulario; si no, campos de solo lectura con badge (CA #5).
/// Reglas (espejo FluentValidation HU-008): valores 0.00..1.00 y umbralVerde &gt; umbralAmarillo
/// estricto en cada categoría. El umbral rojo es implícito (valor &lt; umbralAmarillo, D3).
/// </summary>
public class UmbralesViewModel
{
    public Guid CicloId { get; set; }
    public string CicloNombre { get; set; } = string.Empty;
    public string CicloEstado { get; set; } = string.Empty;

    public decimal KpiVerde { get; set; }
    public decimal KpiAmarillo { get; set; }

    public decimal PlanVerde { get; set; }
    public decimal PlanAmarillo { get; set; }

    public bool EsEditable { get; set; }
}