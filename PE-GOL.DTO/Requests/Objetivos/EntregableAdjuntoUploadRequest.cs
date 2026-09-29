using System;
using System.Collections.Generic;

namespace PE_GOL.DTO.Requests.Objetivos;

/// <summary>
/// Forma de la petición de subida (multipart) — Spec HU-022, capa 1 de validaciones (STACK-04).
/// <para>
/// Proyección de metadatos de cada <c>IFormFile</c> construida por el controller: NO contiene
/// <c>IFormFile</c> (D-D — <c>PE-GOL.DTO</c> no referencia ASP.NET Core) ni streams. La BLL
/// recibe los archivos por su propio contrato (<see cref="EntregableSubidaRequest"/>) y la BLL es
/// la fuente de verdad (UX-04): este DTO solo cubre la FORMA.
/// </para>
/// <para>
/// <b>Distinción respecto de <see cref="EntregableSubidaRequest"/></b> (que está en el namespace
/// padre): este es el DTO de FORMA que consume el <c>AbstractValidator</c> de la API; aquel es el
/// DTO de VERDAD que consume la BLL, y es el único que lleva <c>TenantId</c>/<c>UserId</c>/<c>Rol</c>.
/// </para>
/// </summary>
public class EntregableAdjuntoUploadRequest
{
    /// <summary>
    /// Acción destino. Viene de la RUTA, no del cuerpo (SEC-06); aquí es solo informativo para
    /// el validator, que lo exige no vacío.
    /// </summary>
    public Guid AccionId { get; set; }

    /// <summary>Entre 1 y 5 archivos (CA #1, RN-020).</summary>
    public List<ArchivoSubidaMetadata> Archivos { get; set; } = new();
}

/// <summary>
/// Metadatos de un archivo subido, sin el binario. Lo proyecta el controller desde el
/// <c>IFormFile</c> que él sí conoce.
/// </summary>
public class ArchivoSubidaMetadata
{
    /// <summary><c>FileName</c> original, sin ruta. Sujeto a <c>SanearNombreArchivo</c> en la BLL (D-E).</summary>
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary><c>ContentType</c> declarado por el cliente, p. ej. <c>"application/pdf"</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes (<c>IFormFile.Length</c>).</summary>
    public long TamanoBytes { get; set; }

    /// <summary>Extensión del nombre, sin punto y en minúsculas (<c>""</c> si no trae).</summary>
    public string ExtensionDeNombre { get; set; } = string.Empty;
}
