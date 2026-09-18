namespace PE_GOL.DTO.Responses;

/// <summary>
/// Umbrales de una categoría de semáforo (Spec HU-008 § DTOs — respuesta de GET/PUT).
/// El umbral rojo NO se expone como campo (D3): es implícito, su corte es exactamente
/// umbralAmarillo. Regla de semáforo (RN-038/RN-027, para frontend y SemaforoHelper futuro):
/// Verde → valor &gt;= umbralVerde · Amarillo → umbralAmarillo &lt;= valor &lt; umbralVerde ·
/// Rojo → valor &lt; umbralAmarillo.
/// </summary>
public class UmbralCategoriaResponse
{
    /// <summary>Umbral verde (≥ X), 2 decimales (ej: 0.90).</summary>
    public decimal UmbralVerde { get; set; }

    /// <summary>Umbral amarillo (≥ Y), 2 decimales (ej: 0.70). Rojo = valor &lt; umbralAmarillo.</summary>
    public decimal UmbralAmarillo { get; set; }

    /// <summary>Fecha de última actualización de la fila (umbral_semaforo.updated_at).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
