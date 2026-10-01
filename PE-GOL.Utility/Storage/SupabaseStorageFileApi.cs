using Supabase;
using Supabase.Storage;

namespace PE_GOL.Utility.Storage;

/// <summary>
/// ÚNICA clase del repositorio autorizada a tocar <c>Supabase.Client</c> (D-O, corrección 6
/// del spec HU-022 v3 / enmienda a ADR-013). Envuelve el SDK de Storage 8.1.1 para que
/// <see cref="StorageHelper"/> sea testeable con <c>Mock&lt;IStorageFileApi&gt;</c> sin red
/// ni credenciales.
/// <para>
/// <b>Paquete <c>Supabase</c> 8.1.1</b> (ADR-005): <c>Client</c> construye <c>Storage</c> en su
/// constructor (no requiere <c>InitializeAsync</c> — eso es solo Auth/Realtime);
/// <c>Storage.From(bucket)</c> devuelve un <see cref="StorageFileApi"/> con
/// <c>Upload(byte[], …)</c>, <c>CreateSignedUrl(path, segundos)</c> y <c>Remove(paths)</c>.
/// El cliente se construye <b>server-side</b> con la <c>service_role</c> (RLS-bypass para
/// Storage), sin auto-refresh de sesión ni realtime.
/// </para>
/// <para>
/// <b>Reglas de implementación (ADR-013 § "Reglas de implementación"):</b> el <c>bucket</c> se
/// pasa directo a <c>Storage.From(bucket)</c> — sin transformaciones, sin allowlist y sin
/// interpolación de SQL (SEC-05)—; las rutas son relativas a la raíz del bucket; y
/// <c>Remove</c> devuelve <c>false</c> en vez de lanzar (degradación elegante D-I).
/// </para>
/// </summary>
public sealed class SupabaseStorageFileApi : IStorageFileApi
{
    private readonly Lazy<Supabase.Client> _client;

    /// <summary>
    /// Cliente ya construido por el host (composición explícita). Server-side storage-only:
    /// el paquete `Supabase` 8.1.1 construye `Storage` en el constructor del `Client` y no
    /// exige `InitializeAsync` (eso es solo Auth/Realtime).
    /// </summary>
    public SupabaseStorageFileApi(Supabase.Client client)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        _client = new Lazy<Supabase.Client>(() => client);
    }

    /// <summary>
    /// Constructor que construye el cliente desde la configuración de Storage. Es el que usa el
    /// IOC: mantiene <c>Supabase.Client</c> <b>confinado a esta clase</b> (D-O), de modo que el
    /// contenedor de dependencias de `PE-GOL.IOC` —capa superior a `PE-GOL.Utility`— no necesita
    /// referenciar el SDK de Supabase para registrar nada (ARCH-02). El cliente se construye
    /// <b>perezoso</b> y una sola vez por proceso, igual que el <c>Lazy</c> que tenía
    /// `StorageHelper` antes de la extracción.
    /// </summary>
    public SupabaseStorageFileApi(SupabaseStorageOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _client = new Lazy<Supabase.Client>(() => new Supabase.Client(
            options.Url,
            options.ServiceKey,
            new SupabaseOptions
            {
                AutoRefreshToken = false,
                AutoConnectRealtime = false
            }));
    }

    /// <inheritdoc />
    public async Task<StorageUploadResult?> UploadAsync(string bucket, string path, Stream content,
                                                          FileUploadOptions? options,
                                                          CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("El bucket es obligatorio.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("La ruta del objeto es obligatoria.", nameof(path));
        ArgumentNullException.ThrowIfNull(content);

        ct.ThrowIfCancellationRequested();

        // StorageFileApi 8.1.1 NO expone Upload(Stream, …): se copia a byte[] (adjuntos ≤ 20 MB
        // y logo ≤ 2 MB, ambos validados en BLL antes de llegar aquí — RN-020).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var fileOptions = new Supabase.Storage.FileOptions
        {
            // Upsert = false por defecto (FileUploadOptions): el uuid de la ruta garantiza
            // unicidad y sobrescribir un adjunto sería corrupción de datos. El logo de HU-006
            // lo pide explícitamente en true (D4/ADR-005).
            Upsert = options?.Upsert ?? false
        };

        // ContentType es no nulable en el SDK 8.1.1: solo se asigna si el dominio lo fijó, para
        // no enviar una cabecera vacía que el Storage interpretaría como el tipo real.
        if (!string.IsNullOrWhiteSpace(options?.ContentType))
            fileOptions.ContentType = options!.ContentType!;

        // ── LIMITACIÓN CONOCIDA DEL SDK (a verificar con @Arquitecto/@Orquestador) ───────────────
        // `Supabase.Storage.FileOptions` 8.1.1 NO tiene campo de content-disposition (comprobado
        // en el ensamblado: no existen ni `ContentDisposition` ni la clave `content-disposition`).
        // Por eso `FileUploadOptions.ContentDisposition` —que StorageHelper calcula con
        // TipoArchivoHelper.ContentDisposition(tipo), D-P— NO se puede volcar al Upload con este
        // SDK: el contrato se respeta íntegro en la capa de dominio (que es la que los tests
        // verifican con Mock<IStorageFileApi>), pero el objeto almacenado en Supabase Storage no
        // herdará la cabecera y el navegador decidirá cómo servir el PDF. La sesión de HU-022
        // no puede dejar esto sin decidir: o ADR que suba el SDK / use la API REST cruda de
        // Storage, o aceptación explícita de que el content-disposition no viaja. NO se finge
        // que se envía.
        var rutaPersistida = await _client.Value.Storage
            .From(bucket)
            .Upload(bytes, path, fileOptions, cancellationToken: ct);

        // El SDK no lanza cuando la API de Storage responde con error: Upload devuelve la clave
        // persistida y una cadena vacía/nula si no confirmó. Sin confirmación no hay resultado
        // → null, que es la señal de compensación de la BLL (D-H, RNF-014).
        if (string.IsNullOrWhiteSpace(rutaPersistida))
            return null;

        // La clave se devuelve TAL COMO LA DEVUELVE la API de Supabase, que la antepone al nombre
        // del bucket ("entregables/{tenant}/..."). Quitarle ese prefijo es cosa de
        // <see cref="StorageHelper"/>, que es quien conoce la convención de rutas relativas a la
        // raíz del bucket (ADR-013) y quien devuelve la ruta a la BLL (defecto I, HU-022-hotfix v2).

        // El ETag no lo expone el Upload del SDK 8.1.1 (devuelve la clave, no el cuerpo de la
        // respuesta), así que viaja como null: quien necesite verificar la integridad lo pide
        // con la API de metadatos del bucket, no con un segundo viaje en cada subida.
        return new StorageUploadResult(rutaPersistida, bytes.LongLength, null);
    }

    /// <inheritdoc />
    public async Task<string?> CreateSignedUrlAsync(string bucket, string path, TimeSpan expiresIn,
                                                     CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("El bucket es obligatorio.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(path))
            return null;

        ct.ThrowIfCancellationRequested();

        // El dominio habla en horas (ADR-013) y la capa de bajo nivel en TimeSpan; aquí se
        // redondea al segundo entero que exige el SDK (86 400 s = 24 h, ARCH-06).
        var segundos = (int)Math.Ceiling(expiresIn.TotalSeconds);

        // CreateSignedUrl (8.1.1) no acepta CancellationToken: la comprobación honesta se
        // hace antes, igual que en el wrapper de HU-006.
        return await _client.Value.Storage
            .From(bucket)
            .CreateSignedUrl(path, segundos);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string bucket, string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("El bucket es obligatorio.", nameof(bucket));
        if (string.IsNullOrWhiteSpace(path))
            return false;

        ct.ThrowIfCancellationRequested();

        // Remove(List<string>) devuelve la lista de objetos borrados y null cuando la API de
        // Storage no confirma nada (no existía o error). No se convierte en excepción: la fila
        // de BD ya se borró y el llamador degrada con elegancia (D-I, RNF-014).
        var borrados = await _client.Value.Storage
            .From(bucket)
            .Remove(new List<string> { path });

        return borrados is { Count: > 0 };
    }
}
