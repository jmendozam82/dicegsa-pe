using PE_GOL.Utility.Files;

namespace PE_GOL.Utility.Storage;

/// <summary>
/// Helper de Storage de <c>PE-GOL.Utility</c>: los <b>2 wrappers de dominio de HU-006</b>
/// (logo) y los <b>3 genéricos de ADR-013</b> (cualquier bucket, parametrizado).
/// Se registra en IOC como <b>Singleton</b> (stateless, enlaza SupabaseStorageOptions desde
/// appsettings).
/// <para>
/// <b>HU-006 intacto:</b> <see cref="SubirLogoAsync"/> y <see cref="ObtenerUrlFirmadaAsync(string, TimeSpan, CancellationToken)"/>
/// conservan firma y comportamiento (upsert sobre <c>logos-tenant</c>, ruta <c>{tenant_id}/logo{ext}</c>);
/// ahora solo delegan en la capa de bajo nivel en vez de construir el SDK.
/// </para>
/// <para>
/// <b>Capa de bajo nivel (D-O, corrección 6 / enmienda a ADR-013):</b> esta clase <b>ya no
/// toca <c>Supabase.Client</c></b>. Habla con el Storage a través de
/// <see cref="IStorageFileApi"/>, inyectado, de modo que los tests la ejercitan con
/// <c>Mock&lt;IStorageFileApi&gt;</c> sin red ni credenciales. La única implementación que
/// construye el cliente es <see cref="SupabaseStorageFileApi"/>.
/// </para>
/// </summary>
public class StorageHelper : IStorageHelper
{
    private readonly SupabaseStorageOptions _options;
    private readonly IStorageFileApi _fileApi;

    public StorageHelper(SupabaseStorageOptions options, IStorageFileApi fileApi)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileApi = fileApi ?? throw new ArgumentNullException(nameof(fileApi));
    }

    // ══════════════════════ HU-006 · wrappers de dominio (INTACTOS) ══════════════════════

    /// <summary>
    /// Sube/sobrescribe el logo del tenant (upsert) al bucket `logos-tenant`, ruta
    /// `{tenant_id}/logo{ext}` (D4/ADR-005). Retorna el path de storage (ej: "{tenantId}/logo.png").
    /// </summary>
    public async Task<string> SubirLogoAsync(Guid tenantId, Stream stream, string extension, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var path = $"{tenantId}/logo{extension}";

        // Upsert = true (D4/ADR-005): reemplazar el logo no debe dejar huérfanos. Sin
        // ContentType ni ContentDisposition, exactamente como antes del refactor.
        var resultado = await _fileApi.UploadAsync(
            _options.LogoBucket, path, stream, new FileUploadOptions(Upsert: true), ct);

        // El contrato de HU-006 devuelve string (no nulable) y no distingue el fallo del
        // Storage: se mantiene devolviendo la ruta pedida. El observador del fallo es el
        // propio SDK, que lanza ante error de red.
        return resultado?.Path ?? path;
    }

    /// <summary>
    /// Genera URL firmada del logo con expiración configurable (24 h por defecto — ARCH-06/D5).
    /// Retorna null si el path es null/vacío.
    /// </summary>
    public Task<string?> ObtenerUrlFirmadaAsync(string path, TimeSpan expiracion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<string?>(null);

        // El dominio de HU-006 habla en TimeSpan y el genérico en horas (ADR-013); se redondea
        // AL ALZA al completar la hora, que es la semántica que fija el ADR ((int)Math.Ceiling(horas * 3600)).
        var horas = (int)Math.Ceiling(expiracion.TotalHours);
        return ObtenerUrlFirmadaAsync(_options.LogoBucket, path, horas, ct);
    }

    // ══════════════════════ Genéricos (ADR-013 · spec HU-022 v3) ═══════════════════════════

    /// <summary>
    /// Sube un archivo al bucket indicado, en la ruta indicada (sin barra inicial: ARCH-06 la
    /// escribe con barra por notación de URL, la clave de Storage es relativa a la raíz).
    /// Devuelve el path persistido, o <c>null</c> si el Storage no confirma la subida.
    /// </summary>
    public async Task<string?> SubirArchivoAsync(string bucket, string path, byte[] bytes,
                                                  TipoArchivo tipo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // D-P / bloqueo 8: el content-disposition lo DECIDE el helper a partir del tipo
        // canónico detectado, y el MIME que se persiste es el canónico de la allowlist,
        // nunca el ContentType crudo del navegador. Upsert = false (el uuid de la ruta
        // garantiza unicidad: sobrescribir un adjunto sería corrupción de datos).
        var opciones = new FileUploadOptions(
            ContentType: TipoArchivoHelper.MimeCanonica(tipo),
            ContentDisposition: TipoArchivoHelper.ContentDisposition(tipo),
            Upsert: false);

        using var contenido = new MemoryStream(bytes, writable: false);
        var resultado = await _fileApi.UploadAsync(bucket, path, contenido, opciones, ct);

        // El StorageUploadResult se colapsa a la ruta o a null: la forma del SDK no sube a
        // la BLL (D-O). null = señal de compensación (D-H).
        return string.IsNullOrWhiteSpace(resultado?.Path) ? null : resultado!.Path;
    }

    /// <summary>
    /// URL firmada del objeto del bucket indicado, con expiración en HORAS (ARCH-06: 24 h).
    /// Devuelve null si <paramref name="path"/> es null/vacío. Nunca devuelve una URL pública.
    /// </summary>
    public Task<string?> ObtenerUrlFirmadaAsync(string bucket, string path, int horas, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<string?>(null);

        return _fileApi.CreateSignedUrlAsync(bucket, path, TimeSpan.FromHours(horas), ct);
    }

    /// <summary>
    /// Elimina el objeto del bucket indicado. <c>false</c> = no existía o Supabase reportó
    /// error; no se lanza ni se maquilla como éxito (D-I, RNF-014).
    /// </summary>
    public Task<bool> EliminarArchivoAsync(string bucket, string path, CancellationToken ct = default)
        => _fileApi.RemoveAsync(bucket, path, ct);
}
