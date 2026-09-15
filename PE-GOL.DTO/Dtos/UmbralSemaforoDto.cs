namespace PE_GOL.DTO.Dtos;

/// <summary>
/// DTO de inserción de umbral de semáforo (DAL-C9 del spec HU-007).
/// NOTA de implementación: se añaden CicloId y TenantId (no declarados en el spec) porque
/// DAL-C9 inserta en umbral_semaforo (ciclo_id, tenant_id, tipo, umbral_verde, umbral_amarillo)
/// y toda query incluye el tenant del contexto (SEC-06). La BLL los resuelve del flujo
/// (ciclo recién creado/clonado + TenantContext).
/// </summary>
public class UmbralSemaforoDto
{
    public Guid CicloId { get; set; }
    public Guid TenantId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public decimal UmbralVerde { get; set; }
    public decimal UmbralAmarillo { get; set; }
}