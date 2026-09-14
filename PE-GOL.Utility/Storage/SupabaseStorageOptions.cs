namespace PE_GOL.Utility.Storage;

/// <summary>
/// Opciones de configuración de StorageHelper (ADR-005, HU-006) — enlazadas desde la
/// sección "Supabase" de appsettings en el IOC (Singleton).
/// El paquete `Supabase` expone `SupabaseOptions` (auth/realtime/storage del client) pero NO
/// tiene propiedad para el bucket del logo → wrapper propio con las 3 claves que StorageHelper
/// necesita: Url, ServiceKey (service_role, server-side) y LogoBucket (`logos-tenant`).
/// </summary>
public class SupabaseStorageOptions
{
    /// <summary>Supabase:Url — URL del proyecto (ej: https://xxx.supabase.co).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Supabase:ServiceKey — service_role key (server-side, RLS-bypass para storage).</summary>
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Supabase:LogoBucket — bucket privado del logo de empresa (`logos-tenant`), NUNCA StorageBucket.</summary>
    public string LogoBucket { get; set; } = "logos-tenant";
}