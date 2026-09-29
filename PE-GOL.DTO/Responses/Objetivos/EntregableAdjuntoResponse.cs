using System;

namespace PE_GOL.DTO.Responses.Objetivos;

/// <summary>
/// Un adjunto de una acción — Spec HU-022, CA #3.
/// <para>
/// <b>NO expone <c>TenantId</c> (SEC-06) ni <c>FilePath</c> (D-H: el path de Storage es interno
/// y el acceso es solo por URL firmada de 24 h)</b>. Verificado por reflexión en los casos 35 y
/// 40 de los tests TDD.
/// </para>
/// <para>
/// Expone <see cref="PuedeEliminar"/> para que la UI no reimplemente la regla de autorización
/// del CA #5 (calculada en la BLL, DB-04).
/// </para>
/// </summary>
public class EntregableAdjuntoResponse
{
    /// <summary>UUID del adjunto.</summary>
    public Guid Id { get; set; }

    /// <summary>UUID de la acción a la que pertenece.</summary>
    public Guid AccionId { get; set; }

    /// <summary>CA #3 — nombre original saneado (sin ruta, sin caracteres de control, máximo 255).</summary>
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>CA #3 — tamaño en bytes (columna <c>file_size_bytes</c>, INT).</summary>
    public long TamanoBytes { get; set; }

    /// <summary>
    /// Tamaño formateado para la vista (<c>"1,4 MB"</c>) — CALCULADO en la BLL con
    /// <c>EntregableAdjuntoReglas.FormatearTamano</c> (DB-04, corrección 4). NO existe en la
    /// entidad ni en el snapshot de auditoría.
    /// </summary>
    public string TamanoLegible { get; set; } = string.Empty;

    /// <summary>
    /// CA #2 — MIME canónico del tipo permitido (columna <c>tipo_mime</c>), normalizado por la BLL.
    /// Nunca el <c>ContentType</c> crudo del navegador.
    /// </summary>
    public string TipoMime { get; set; } = string.Empty;

    /// <summary>
    /// Extensión normalizada en minúsculas y sin punto (<c>".pdf"</c> → <c>"pdf"</c>) — CALCULADO
    /// en la BLL. Si el nombre no trae extensión, la BLL proyecta <c>"bin"</c> solo para visualizar.
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// CA #3 — «usuario que subió»: <c>usuario.nombre</c> vía <c>LEFT JOIN</c> (corrección 3; el
    /// DDL NO tiene <c>nombre_completo</c>). <c>null</c> si el usuario fue borrado; la vista
    /// muestra «—».
    /// </summary>
    public string? SubidoPorNombre { get; set; }

    /// <summary>CA #3 — «fecha de subida» (columna <c>created_at</c>, TIMESTAMPTZ, en UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// CA #5 — <c>true</c> si el usuario actual puede eliminarlo (autor o Gerente). CALCULADO en
    /// la BLL con la MISMA regla que <c>EliminarAsync</c>, para que la UI no la reimplemente.
    /// </summary>
    public bool PuedeEliminar { get; set; }
}
