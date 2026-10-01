using System.IO.Compression;
using System.Text;

namespace PE_GOL.Utility.Files;

/// <summary>
/// Tipo canónico de un adjunto (CA #2 de HU-022, RN-020). Enum CERRADO a propósito
/// (bloqueo 4 de la revisión v3 del spec): la BLL no compara literales de MIME sueltos,
/// compara valores de este enum, y el compilador obliga a cubrir el caso <c>Desconocido</c>
/// en cada <c>switch</c>. El tipo se decide por la FIRMA DE BYTES real del contenido
/// (RNF-009), nunca por el nombre ni por el <c>ContentType</c> que envía el navegador.
/// </summary>
public enum TipoArchivo
{
    /// <summary>PDF — firma <c>%PDF</c>. Se sirve <c>inline</c>.</summary>
    Pdf,

    /// <summary>DOCX — ZIP con <c>word/</c> en el directorio central. Se sirve <c>attachment</c>.</summary>
    Docx,

    /// <summary>XLSX — ZIP con <c>xl/</c> en el directorio central. Se sirve <c>attachment</c>.</summary>
    Xlsx,

    /// <summary>PNG — firma <c>89 50 4E 47 0D 0A 1A 0A</c>. Se sirve <c>attachment</c>.</summary>
    Png,

    /// <summary>JPG — firma <c>FF D8 FF</c>. Se sirve <c>attachment</c>.</summary>
    Jpg,

    /// <summary>Ninguna firma permitida. La BLL lo rechaza con 422 ANTES de subir (D-H: todo o nada).</summary>
    Desconocido
}

/// <summary>
/// Detección de tipo por firma de bytes + allowlists canónicas de HU-022
/// (spec v3 § "Detección de tipo", ARCH-06, RN-020, RNF-009).
/// <para>
/// <b>Por qué vive en <c>PE-GOL.Utility/Files/</c>:</b> es nuevo en HU-022 (no existía;
/// <c>PE-GOL.Utility/Helpers/</c> solo tenía <c>CicloFechaHelper</c>, <c>SemaforoHelper</c>
/// y <c>ZonaHorariaHelper</c>) y agrupa en un solo artefacto el enum, la firma de bytes y
/// las allowlists, que son la MISMA fuente de verdad (D-L): ni literales en C#, ni en
/// Razor, ni en JS.
/// </para>
/// <para>
/// <b>Responsabilidad:</b> solo <b>constantes + detección determinista sin red</b>. NO valida
/// negocio (ni tamaños, ni conteos, ni pertenencia a un ciclo): eso vive exclusivamente en
/// <c>PE-GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs</c>, porque <c>PE-GOL.Utility</c>
/// no puede referenciar <c>PE-GOL.BLL</c> (ARCH-02) y las reglas necesitan <c>TimeProvider</c>
/// y <c>ValidacionException</c> de la BLL.
/// </para>
/// </summary>
public static class TipoArchivoHelper
{
    // ── Allowlists (única fuente: ni literales en C#, Razor ni JS — D-L) ──────────

    /// <summary>CA #2 — las 5 extensiones permitidas, en minúsculas y SIN punto.</summary>
    public static readonly IReadOnlyList<string> ExtensionesPermitidas =
        new[] { "pdf", "docx", "xlsx", "png", "jpg" };

    /// <summary>CA #2 — los 5 MIME canónicos de la allowlist (nunca el <c>ContentType</c> crudo).</summary>
    public static readonly IReadOnlyList<string> MimesPermitidos =
        new[]
        {
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/png",
            "image/jpeg"
        };

    /// <summary>
    /// Alias tolerados (D-C): Chrome/Windows los envían en <c>.docx</c>/<c>.xlsx</c>.
    /// Tolerarlos evita falsos rechazos, pero <b>NO relajan la mitigación de spoofing</b>:
    /// el paso de la BLL sigue verificando la firma real del contenido (RNF-009).
    /// </summary>
    public static readonly IReadOnlyList<string> MimesGenericosTolerados =
        new[] { "application/octet-stream", "application/zip" };

    /// <summary>
    /// CA #2 / RNF-009 — firma de bytes real. Devuelve el enum canónico (bloqueo 4);
    /// <see cref="TipoArchivo.Desconocido"/> si la cabecera no corresponde a ninguno de los
    /// 5 tipos permitidos.
    /// <para>
    /// DOCX y XLSX comparten la firma <c>PK\x03\x04</c> y se desambiguan con el
    /// <b>directorio central del ZIP</b> (<c>word/</c> → DOCX, <c>xl/</c> → XLSX).
    /// Cuando el buffer recibido está <b>truncado</b> (la BLL lee solo los primeros ~512
    /// bytes) el directorio central no cabe, así que se cae a la búsqueda de las rutas de
    /// entrada en las cabeceras locales del ZIP, que sí están al principio del archivo.
    /// Un ZIP que no contenga ni <c>word/</c> ni <c>xl/</c> devuelve <c>Desconocido</c>:
    /// no se adivina por extensión (RNF-009).
    /// </para>
    /// </summary>
    public static TipoArchivo DetectarTipo(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return TipoArchivo.Pdf; // %PDF

        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return TipoArchivo.Png;

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return TipoArchivo.Jpg;

        if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            return DetectarTipoOfficeOpenXml(header);

        return TipoArchivo.Desconocido;
    }

    /// <summary>
    /// DOCX vs XLSX (ambos OOXML = ZIP). Primero el directorio central, que es la fuente
    /// autoritativa; si el buffer está truncado, las cabeceras locales del ZIP (que están
    /// al principio) delatan las mismas rutas.
    /// <para>
    /// Con el archivo COMPLETO no hace falta el respaldo: <see cref="ZipArchive"/> encuentra el
    /// directorio central al final del archivo. Ese es el motivo de leer el archivo entero y no
    /// una cabecera corta (defecto H, HU-022-hotfix v2).
    /// </para>
    /// </summary>
    private static TipoArchivo DetectarTipoOfficeOpenXml(ReadOnlySpan<byte> header)
    {
        var hayWord = false;
        var hayXl = false;

        try
        {
            using var ms = new MemoryStream(header.ToArray(), writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entrada in zip.Entries)
            {
                // word/  → DOCX · xl/  → XLSX (comparación con ordinal: rutas del ZIP).
                if (!hayWord && entrada.FullName.StartsWith("word/", StringComparison.Ordinal)) hayWord = true;
                if (!hayXl && entrada.FullName.StartsWith("xl/", StringComparison.Ordinal)) hayXl = true;
                if (hayWord || hayXl) break;
            }
        }
        catch (InvalidDataException)
        {
            // Buffer truncado: el directorio central no está. No es un fallo de validación:
            // se resuelve con el paso de respaldo de abajo.
        }
        catch (ArgumentException)
        {
            // ZIP con estructura inválida: mismo tratamiento que el truncamiento.
        }

        if (!hayWord && !hayXl)
        {
            // Respaldo: los nombres de archivo de las cabeceras locales ("word/document.xml",
            // "xl/workbook.xml") están en crudo dentro de los primeros bytes del ZIP.
            var crudo = Encoding.ASCII.GetString(header);
            hayWord = crudo.Contains("word/", StringComparison.Ordinal);
            hayXl = crudo.Contains("xl/", StringComparison.Ordinal);
        }

        if (hayWord) return TipoArchivo.Docx;
        if (hayXl) return TipoArchivo.Xlsx;
        return TipoArchivo.Desconocido;
    }

    /// <summary>
    /// <c>content-disposition</c> del objeto (D-P, bloqueo 8). Decide por el <b>TIPO canónico
    /// detectado</b>, no por el nombre ni por el MIME crudo del navegador.
    /// <para>
    /// PDF se abre en pestaña (<c>inline</c>); el resto se fuerza a descarga
    /// (<c>attachment</c>). <b>Nunca lleva <c>filename=</c></b>: el nombre visible es la
    /// columna <c>nombre_archivo</c> (saneada, D-E) y la ruta de Storage va con UUID;
    /// duplicar la fuente de verdad abriría la puerta a inyectar cabeceras (RNF-009).
    /// </para>
    /// <para>
    /// <b>Consecuencia asumida en la v3 del spec:</b> PNG y JPG se descargan en lugar de
    /// abrirse en pestaña.
    /// </para>
    /// </summary>
    public static string ContentDisposition(TipoArchivo tipo) => tipo switch
    {
        TipoArchivo.Pdf => "inline",                       // se abre en pestaña
        TipoArchivo.Docx or TipoArchivo.Xlsx => "attachment", // fuerza la descarga
        _ => "attachment"                                   // Png, Jpg y Desconocido
    };

    /// <summary>
    /// Extensión canónica (minúscula, sin punto) del tipo resuelto, para la ruta de Storage
    /// (<c>{tenant}/{ciclo}/{accion}/{uuid}.{ext}</c>, ARCH-06) y para el DTO.
    /// <para>
    /// Prioridad: (1) la extensión del nombre, si está en la allowlist; (2) el MIME canónico
    /// del navegador, si es uno de la allowlist — que es como un <c>.docx</c> enviado como
    /// <c>application/octet-stream</c> (D-C) conserva su extensión; (3) <c>"bin"</c>, que es
    /// un valor de visualización, no un tipo permitido (la BLL ya rechazó antes lo que no lo es).
    /// </para>
    /// </summary>
    public static string ExtensionCanonica(string extensionNombre, string contentType)
    {
        var extension = (extensionNombre ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (ExtensionesPermitidas.Contains(extension, StringComparer.Ordinal))
            return extension;

        var mime = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        var porMime = mime switch
        {
            "application/pdf" => "pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            _ => null
        };

        return porMime ?? "bin";
    }

    /// <summary>
    /// MIME canónico del tipo resuelto. <b>Nunca</b> devuelve el <c>ContentType</c> crudo del
    /// navegador: un <c>application/octet-stream</c> no se persiste como MIME del dominio.
    /// </summary>
    public static string MimeCanonica(TipoArchivo tipo) => tipo switch
    {
        TipoArchivo.Pdf => "application/pdf",
        TipoArchivo.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        TipoArchivo.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        TipoArchivo.Png => "image/png",
        TipoArchivo.Jpg => "image/jpeg",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// Nombre legible de un tipo, para el mensaje de rechazo (RNF-009). Sin esto el usuario
    /// solo lee «el contenido no corresponde a su extensión» y no sabe qué tiene entre manos:
    /// el caso real fue un <c>.doc</c> de Word 97-2003 renombrado a <c>.docx</c>, que la
    /// validación bloquea correctamente pero sin explicar por qué.
    /// </summary>
    public static string NombreLegible(TipoArchivo tipo) => tipo switch
    {
        TipoArchivo.Pdf => "PDF",
        TipoArchivo.Docx => "DOCX de Word (OOXML)",
        TipoArchivo.Xlsx => "XLSX de Excel (OOXML)",
        TipoArchivo.Png => "PNG",
        TipoArchivo.Jpg => "JPG",
        _ => "un formato no permitido"
    };
}
