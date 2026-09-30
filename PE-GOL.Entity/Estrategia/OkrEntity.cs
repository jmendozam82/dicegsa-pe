namespace PE_GOL.Entity.Estrategia;

/// <summary>
/// Entidad OKR (Objectives and Key Results) del área para un ciclo activo (Spec HU-024).
/// Mapea la tabla okr definida en 06_MODELO_DATOS.md L233-247.
/// </summary>
public class OkrEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CicloId { get; set; }
    public Guid AreaId { get; set; }
    public Guid PilarId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PuntuacionFinal { get; set; }
    public string Semaforo { get; set; } = string.Empty;
    public int Orden { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
