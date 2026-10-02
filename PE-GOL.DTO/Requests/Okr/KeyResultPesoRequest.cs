namespace PE_GOL.DTO.Requests.Okr;

/// <summary>Peso individual dentro de la actualización masiva — Spec HU-025 § F0 (Opción B, aprobada).
/// Sin TenantId/AreaId/CicloId/OkrId (SEC-06): el okr_id viaja solo en la ruta del endpoint.</summary>
public class KeyResultPesoRequest
{
    /// <summary>Id del Key Result a actualizar.</summary>
    public Guid Id { get; set; }

    /// <summary>Nuevo peso del KR. Rango (0, 1] con escala de 3 decimales.</summary>
    public decimal Peso { get; set; }
}
