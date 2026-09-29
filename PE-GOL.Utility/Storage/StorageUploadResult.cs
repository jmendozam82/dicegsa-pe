namespace PE_GOL.Utility.Storage;

/// <summary>
/// Resultado de la subida (ADR-013, spec HU-022 v3 — bloqueo 3).
/// <para>
/// <b>Por qué un record y no solo el <c>path</c>:</b> <see cref="Path"/> y <see cref="ETag"/>
/// son verificables sin red (el ETag es la versión/ETag que devuelve el Storage y sirve para
/// comprobar integridad), y <see cref="SizeBytes"/> lo persistirá la BLL en
/// <c>entregable_adjunto.file_size_bytes</c>. Devolver <c>null</c> —en lugar de lanzar— es la
/// señal de fallo que activa la compensación (D-H, RNF-014).
/// </para>
/// <para>
/// <b>No se persiste este record</b>: la capa de dominio guarda <c>nombre_archivo</c>,
/// <c>file_path</c>, <c>file_size_bytes</c> y <c>tipo_mime</c> (06_MODELO_DATOS L314-325);
/// <c>StorageHelper</c> colapsa este tipo a la ruta o a <c>null</c> para no filtrar la forma
/// del SDK hacia la BLL.
/// </para>
/// </summary>
/// <param name="Path">Clave persistida en el bucket (la que devolvió el Storage, no la solicitada).</param>
/// <param name="SizeBytes">Tamaño del objeto en bytes (columna <c>file_size_bytes</c> del DDL).</param>
/// <param name="ETag">Versión/ETag del objeto. <c>null</c> si el Storage no la informa.</param>
public sealed record StorageUploadResult(
    string Path,
    long SizeBytes,
    string? ETag);
