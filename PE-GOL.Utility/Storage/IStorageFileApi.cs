namespace PE_GOL.Utility.Storage;

/// <summary>
/// Abstracción de bajo nivel sobre Supabase Storage (ADR-013, spec HU-022 v3 § Dependencias).
/// <para>
/// <b>Por qué existe (D-O, corrección 6):</b> <c>StorageHelper</c> construía su
/// <c>Supabase.Client</c> en su propio constructor (<c>Lazy&lt;Supabase.Client&gt;</c> interno),
/// de modo que era <b>imposible testearlo sin red</b>: los tests de HU-006 lo cubrieron por
/// reflexión sobre la clase, no por comportamiento. Al inyectar esta interfaz,
/// <c>StorageHelper</c> es testeable con <c>Mock&lt;IStorageFileApi&gt;</c> y
/// <c>Supabase.Client</c> queda <b>confinado a una sola clase</b> del repositorio
/// (<see cref="SupabaseStorageFileApi"/>).
/// </para>
/// <para>
/// <b>Contrato de los 3 tipos</b> (<c>FileUploadOptions</c>, <c>StorageUploadResult</c>):
/// declarados aquí con firma exacta porque la v1/v2 del spec los nombraba sin declararlos
/// (bloqueo 3) y los tests no tenían contrato contra el que escribir.
/// </para>
/// <para>
/// <b>Límite de capas:</b> esta interfaz habla <b>rutas y bytes</b>, no entidades ni reglas de
/// negocio. El <c>bucket</c> es siempre un nombre literal del código o de la configuración
/// (ADR-013 § "Reglas de implementación"); <b>jamás</b> viene del cliente (SEC-06). Los
/// retornos usan <c>null</c>/<c>false</c> —no excepciones— como <b>señal de fallo</b> para que
/// la BLL compense sin envolver todo el flujo (RNF-014, degradación elegante).
/// </para>
/// </summary>
public interface IStorageFileApi
{
    /// <summary>
    /// Sube <paramref name="content"/> a <c><paramref name="bucket"/>/<paramref name="path"/></c>
    /// (ruta <b>relativa a la raíz del bucket</b>, sin barra inicial: <c>{tenant}/{ciclo}/{accion}/{uuid}.{ext}</c>,
    /// ARCH-06; la barra inicial de la arquitectura es notación de URL, no de clave).
    /// </summary>
    /// <param name="bucket">Nombre físico del bucket. No es una ruta ni una expresión SQL (SEC-05).</param>
    /// <param name="path">Clave del objeto dentro del bucket.</param>
    /// <param name="content">Contenido a subir. El SDK 8.1.1 solo expone <c>Upload(byte[], …)</c>: la copia a buffer la hace la implementación.</param>
    /// <param name="options">Opciones (<c>ContentType</c>, <c>ContentDisposition</c>, <c>Upsert</c>). <c>null</c> = las que-marque el SDK.</param>
    /// <param name="ct">Cancelación. En la compensación de errores el llamador debe pasar <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// El resultado con la ruta persistida, el tamaño y el ETag; <c>null</c> si el Storage no
    /// confirma la subida — es la señal de fallo que la BLL usa para compensar y no insertar
    /// filas (D-H: todo o nada por lote).
    /// </returns>
    Task<StorageUploadResult?> UploadAsync(string bucket, string path, Stream content,
                                            FileUploadOptions? options, CancellationToken ct = default);

    /// <summary>
    /// URL firmada de acceso temporal. <paramref name="expiresIn"/> es la expiración
    /// (ARCH-06: 24 h = <c>TimeSpan.FromHours(24)</c> = 86 400 s). <b>Nunca devuelve una URL
    /// pública</b>: los buckets son privados (RNF-008).
    /// </summary>
    /// <returns>La URL firmada; <c>null</c> si el Storage no la emite (la BLL degrada a <c>Url = null</c>, D-G, no a 500).</returns>
    Task<string?> CreateSignedUrlAsync(string bucket, string path, TimeSpan expiresIn, CancellationToken ct = default);

    /// <summary>
    /// Elimina el objeto del bucket.
    /// </summary>
    /// <returns>
    /// <c>false</c> = no existía o el Storage reportó error. <b>No se maquilla como éxito y no
    /// lanza</b>: la fila de BD ya se borró y el llamador aplica degradación elegante + log
    /// (D-I, RNF-014, sin archivos huérfanos silenciosos).
    /// </returns>
    Task<bool> RemoveAsync(string bucket, string path, CancellationToken ct = default);
}
