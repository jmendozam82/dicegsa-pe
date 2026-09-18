namespace PE_GOL.DTO.Requests;

/// <summary>
/// Umbrales de una categoría de semáforo (Spec HU-008 § DTOs — request del PUT).
/// CA #1: KPI y Plan de Acción son categorías independientes → cada una lleva su propio
/// par de umbrales. Reglas (validadas en FluentValidation y re-validadas en BLL, UX-04):
/// umbralVerde/umbralAmarillo ∈ 0.00..1.00 (espejo CHECKs DDL L156-157) y
/// umbralVerde &gt; umbralAmarillo ESTRICTO (espejo CHECK L155; verde igual a amarillo se rechaza).
/// El umbral rojo NO forma parte del request (D3): es implícito (valor &lt; umbralAmarillo).
/// </summary>
public class UmbralCategoriaRequest
{
    /// <summary>Requerido, 0.00..1.00, estrictamente mayor que UmbralAmarillo.</summary>
    public decimal UmbralVerde { get; set; }

    /// <summary>Requerido, 0.00..1.00.</summary>
    public decimal UmbralAmarillo { get; set; }
}
