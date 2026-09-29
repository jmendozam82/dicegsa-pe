namespace PE_GOL.Utility.Storage;

/// <summary>
/// Opciones de configuración del Storage (ADR-005 HU-006 + ADR-013 HU-022) — enlazadas desde la
/// sección "Supabase" de appsettings en el IOC (Singleton).
/// El paquete `Supabase` expone `SupabaseOptions` (auth/realtime/storage del client) pero NO
/// tiene propiedad para los buckets → wrapper propio con las 4 claves que Storage necesita:
/// Url, ServiceKey (service_role, server-side), LogoBucket (`logos-tenant`) y
/// EntregablesBucket (`entregables`).
/// </summary>
public class SupabaseStorageOptions
{
    /// <summary>Supabase:Url — URL del proyecto (ej: https://xxx.supabase.co).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Supabase:ServiceKey — service_role key (server-side, RLS-bypass para storage).</summary>
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Supabase:LogoBucket — bucket privado del logo de empresa (`logos-tenant`), NUNCA StorageBucket.</summary>
    public string LogoBucket { get; set; } = "logos-tenant";

    /// <summary>
    /// Supabase:EntregablesBucket — bucket privado de entregables (`entregables`, ARCH-06).
    /// Default idéntico en patrón al <see cref="LogoBucket"/> de ADR-005: el código funciona sin
    /// configuración y, si la clave está, la configuración gana. Es la clave CANÓNICA del bucket
    /// de adjuntos; `Supabase:StorageBucket` queda obsoleta (ADR-013, por documentar al cerrar HU-022).
    /// </summary>
    public string EntregablesBucket { get; set; } = "entregables";
}
