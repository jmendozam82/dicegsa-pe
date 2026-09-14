using Supabase;
using Supabase.Storage;

namespace PE_GOL.Utility.Storage;

/// <summary>
/// Helper de Supabase Storage para el logo de empresa (ADR-005, HU-006).
/// EXACTAMENTE dos métodos: SubirLogoAsync (upsert) y ObtenerUrlFirmadaAsync (24 h).
/// Bucket del logo: `logos-tenant` (clave `Supabase:LogoBucket`) — NUNCA `StorageBucket`.
/// Se registra en IOC como Singleton (stateless, enlaza SupabaseStorageOptions desde appsettings).
/// Implementación real (fase 4 del Loop); los tests de @QA (EmpresaServiceTests) mockean
/// IStorageHelper — esta clase solo se ejercita en integración/producción.
/// Paquete `Supabase` 8.1.1 (renombrado de `supabase-csharp`, ADR-005): el Client construye
/// Storage en su ctor (no requiere InitializeAsync — eso es solo Auth/Realtime); Storage.From
/// devuelve StorageFileApi con Upload(byte[], path, FileOptions{Upsert}) y CreateSignedUrl.
/// </summary>
public class StorageHelper : IStorageHelper
{
    private readonly SupabaseStorageOptions _options;
    private readonly Lazy<Supabase.Client> _client;

    public StorageHelper(SupabaseStorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new Lazy<Supabase.Client>(() =>
        {
            // Server-side storage-only: sin auto-refresh de sesión ni realtime.
            var supabaseOptions = new SupabaseOptions
            {
                AutoRefreshToken = false,
                AutoConnectRealtime = false
            };
            return new Supabase.Client(_options.Url, _options.ServiceKey, supabaseOptions);
        });
    }

    /// <summary>
    /// Sube/sobrescribe el logo del tenant (upsert) al bucket `logos-tenant`, ruta
    /// `{tenant_id}/logo.{ext}` (D4/ADR-005). Retorna el path de storage (ej: "{tenantId}/logo.png").
    /// </summary>
    public async Task<string> SubirLogoAsync(Guid tenantId, Stream stream, string extension, CancellationToken ct = default)
    {
        var path = $"{tenantId}/logo{extension}";

        // StorageFileApi 8.1.1 no expone Upload(Stream,...): se copia a byte[] (logo ≤ 2 MB,
        // validado en BLL). Upsert = sobrescritura del logo anterior (D4, sin archivos huérfanos).
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        await _client.Value.Storage
            .From(_options.LogoBucket)
            .Upload(bytes, path, new Supabase.Storage.FileOptions { Upsert = true }, cancellationToken: ct);

        return path;
    }

    /// <summary>
    /// Genera URL firmada con expiración configurable (24 h por defecto — ARCH-06/D5).
    /// Retorna null si el path es null/vacío. CreateSignedUrl (8.1.1) no acepta CancellationToken.
    /// </summary>
    public async Task<string?> ObtenerUrlFirmadaAsync(string path, TimeSpan expiracion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        ct.ThrowIfCancellationRequested();

        var segundos = (int)Math.Ceiling(expiracion.TotalSeconds);
        return await _client.Value.Storage
            .From(_options.LogoBucket)
            .CreateSignedUrl(path, segundos);
    }
}