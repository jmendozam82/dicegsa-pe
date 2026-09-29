namespace PE_GOL.Utility.Storage;

/// <summary>
/// Contrato de StorageHelper (ADR-005 HU-006, ampliado por ADR-013 / spec HU-022 v3).
/// <para>
/// <b>Estructura del contrato: 2 métodos de dominio (HU-006) + 3 genéricos (HU-022).</b> Los 2
/// primeros quedan <b>intactos</b> —ni firma ni comportamiento— y ahora delegan en los
/// genéricos vía <see cref="IStorageFileApi"/>, de modo que HU-006 no se entera del refactor.
/// </para>
/// <para>
/// El bucket del logo es `logos-tenant` (clave de configuración `Supabase:LogoBucket`) y el de
/// entregables `entregables` (`Supabase:EntregablesBucket`), NUNCA `Supabase:StorageBucket`
/// (obsoleta). Los buckets fijados los resuelve la implementación desde la configuración
/// (SupabaseStorageOptions); los genéricos los recibe la BLL como argumento —siempre literales
/// del código, nunca del cliente (SEC-06)—. La BLL solo delega en esta interfaz (D13: la BLL
/// no depende de ASP.NET Core ni del SDK de Supabase).
/// </para>
/// </summary>
public interface IStorageHelper
{
    // ── Wrappers de dominio HU-006 (INTACTOS — ADR-005, no cambian firma ni comportamiento) ──

    /// <summary>
    /// Sube/sobrescribe el logo del tenant (upsert) al bucket `logos-tenant`, ruta
    /// `{tenant_id}/logo{ext}` (D4/ADR-005). Retorna el path de storage (ej: "{tenantId}/logo.png").
    /// </summary>
    Task<string> SubirLogoAsync(Guid tenantId, Stream stream, string extension, CancellationToken ct = default);

    /// <summary>
    /// Genera URL firmada del logo con expiración configurable (24 h por defecto — ARCH-06/D5).
    /// Retorna null si el path es null/vacío.
    /// </summary>
    Task<string?> ObtenerUrlFirmadaAsync(string path, TimeSpan expiracion, CancellationToken ct = default);

    // ── Genéricos (nuevos, ADR-013 / spec HU-022 v3) ───────────────────────────────────────────

    /// <summary>
    /// Sube un archivo al bucket indicado, en la ruta indicada (relativa a la raíz del bucket,
    /// sin barra inicial: <c>{tenant_id}/{ciclo_id}/{accion_id}/{uuid}.{ext}</c> — ARCH-06).
    /// <para>
    /// El <c>content-disposition</c> <b>NO es un parámetro</b> (D-P, bloqueo 8): lo decide el
    /// helper a partir del <see cref="Files.TipoArchivo"/> canónico ya detectado
    /// (<c>TipoArchivoHelper.ContentDisposition(tipo)</c>), de modo que un <c>ContentType</c>
    /// del navegador no pueda influir en cómo se sirve el objeto.
    /// </para>
    /// </summary>
    /// <returns>
    /// El path persistido, o <c>null</c> si el Storage no confirma la subida — la señal que
    /// la BLL usa para compensar y no insertar filas (D-H: todo o nada por lote, RNF-014).
    /// </returns>
    Task<string?> SubirArchivoAsync(string bucket, string path, byte[] bytes,
                                     Files.TipoArchivo tipo, CancellationToken ct = default);

    /// <summary>
    /// URL firmada del objeto del bucket indicado, con expiración en <b>HORAS</b> (lo que
    /// expresan ARCH-06 y el dominio; la conversión a segundos la hace el SDK — ADR-013).
    /// <para>
    /// <b>No lleva <c>contentDisposition</c></b> (D-P): el valor quedó persistido en el objeto
    /// durante el <c>Upload</c> y una URL firmada no puede reescribirlo (hacerlo exigiría
    /// re-subir el binario y romper RNF-001). Nunca devuelve una URL pública (RNF-008).
    /// </para>
    /// </summary>
    /// <returns>La URL firmada; <c>null</c> si <paramref name="path"/> es null/vacío o el Storage no la emite.</returns>
    Task<string?> ObtenerUrlFirmadaAsync(string bucket, string path, int horas, CancellationToken ct = default);

    /// <summary>
    /// Elimina el objeto del bucket indicado. <c>false</c> = no existía o Supabase reportó
    /// error: no se lanza, la fila de BD ya se borró y el llamador degrada con elegancia
    /// (D-I, RNF-014 — sin archivos huérfanos silenciosos).
    /// </summary>
    Task<bool> EliminarArchivoAsync(string bucket, string path, CancellationToken ct = default);
}
