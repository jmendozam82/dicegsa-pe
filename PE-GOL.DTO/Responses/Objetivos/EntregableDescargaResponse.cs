using System;

namespace PE_GOL.DTO.Responses.Objetivos;

/// <summary>
/// CA #4 — URL firmada de acceso temporal (ARCH-06: 24 h).
/// <para>
/// El navegador descarga DIRECTO desde Supabase Storage con esta URL; la API nunca hace proxy
/// del binario (RNF-001).
/// </para>
/// <para>
/// <b>NO expone <c>TenantId</c> ni <c>FilePath</c></b> (SEC-06 / D-H) — verificado por reflexión
/// en el caso 40. <b>NO lleva <c>ContentDisposition</c></b> (bloqueo 8): el
/// <c>content-disposition</c> se fijó en la SUBIDA, dentro de <c>FileUploadOptions</c>, y una URL
/// firmada no puede reescribirlo sin re-subir el binario.
/// </para>
/// </summary>
public class EntregableDescargaResponse
{
    /// <summary>UUID del adjunto.</summary>
    public Guid Id { get; set; }

    /// <summary>UUID de la acción propietaria del adjunto.</summary>
    public Guid AccionId { get; set; }

    /// <summary>Nombre visible del archivo (columna <c>nombre_archivo</c>, saneada en D-E).</summary>
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes, para que la UI pueda mostrarlo sin una segunda llamada.</summary>
    public long TamanoBytes { get; set; }

    /// <summary>MIME canónico del tipo permitido (columna <c>tipo_mime</c>).</summary>
    public string TipoMime { get; set; } = string.Empty;

    /// <summary>
    /// URL firmada. <c>null</c> si el Storage falló (RNF-014, degradación elegante, D-G): la BLL
    /// devuelve 200 con esta propiedad nula en lugar de un 500, y la UI muestra un aviso.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Instante de expiración de la URL firmada (UTC) = ahora + 24 h. Se rellena
    /// <b>siempre</b>, incluso en la degradación, para que la UI pueda mostrarlo y para que el
    /// test sea determinista con <c>Mock&lt;TimeProvider&gt;</c>.
    /// </summary>
    public DateTimeOffset ExpiraEn { get; set; }
}
