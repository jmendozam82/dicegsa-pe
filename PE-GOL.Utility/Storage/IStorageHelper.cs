namespace PE_GOL.Utility.Storage;

/// <summary>
/// Contrato de StorageHelper (ADR-005, HU-006) — EXACTAMENTE dos métodos.
/// El bucket del logo es `logos-tenant` (clave de configuración `Supabase:LogoBucket`),
/// NUNCA `Supabase:StorageBucket` (`entregables`, reservado para adjuntos de acciones HU-030+).
/// El bucket se resuelve DENTRO de la implementación desde la configuración (SupabaseOptions);
/// la BLL solo delega en estos dos métodos (D13: la BLL no depende de ASP.NET Core ni de Supabase).
/// Stub de contrato (TDD fase roja, TEST-01): la implementación real es de @BackendDev.
/// </summary>
public interface IStorageHelper
{
    /// <summary>
    /// Sube/sobrescribe el logo del tenant (upsert) al bucket `logos-tenant`, ruta
    /// `{tenant_id}/logo.{ext}` (D4/ADR-005). Retorna el path de storage (ej: "{tenantId}/logo.png").
    /// </summary>
    Task<string> SubirLogoAsync(Guid tenantId, Stream stream, string extension, CancellationToken ct = default);

    /// <summary>
    /// Genera URL firmada con expiración configurable (24 h por defecto — ARCH-06/D5).
    /// Retorna null si el path es null/vacío.
    /// </summary>
    Task<string?> ObtenerUrlFirmadaAsync(string path, TimeSpan expiracion, CancellationToken ct = default);
}