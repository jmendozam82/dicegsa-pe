using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Helpers;

namespace PE_GOL.BLL.Limites;

/// <summary>
/// Reglas de negocio de los entregables adjuntos — Spec HU-022 § Validaciones, capa BLL.
/// <para>
/// <b>Clase NORMAL, sin heredar nada y sin FluentValidation</b> (corrección 5 / D-N):
/// <c>PE-GOL.BLL.csproj</c> no referencia FluentValidation y los 32 <c>AbstractValidator</c> del
/// repositorio viven en <c>PE-GOL.API/Validators/</c>. El <c>AbstractValidator</c> de la API valida
/// la FORMA del request; esta clase valida la VERDAD, y la validación del servidor es siempre la
/// fuente de verdad (UX-04). Precedente exacto: <see cref="PlanLimitValidator"/> — 100 % puro y
/// determinista, sin BD ni I/O, ideal para TDD unitario.
/// </para>
/// <para>
/// <b>Reparto de responsabilidades con <see cref="EntregableAdjuntoReglas"/>:</b> aquel es
/// CONSTANTE + FORMATO (bucket, 5 archivos, 20 MB, 24 h, 255 caracteres, formateo y saneado de
/// nombre) y no puede vivir aquí porque <c>PE-GOL.Utility</c> no referencia a <c>PE-GOL.BLL</c>
/// (ARCH-02). Aquí viven las REGLAS, que necesitan <c>ValidacionException</c>.
/// </para>
/// <para>
/// <b>Reparto interno:</b> <see cref="ValidarTipo"/> decide por la <b>extensión y el MIME</b>
/// (lo que declara el cliente, incluida la tolerancia a los MIME genéricos de Chrome/Windows);
/// la <b>firma de bytes real</b> no se comprueba aquí sino en el servicio, que es quien tiene los
/// bytes (<c>TipoArchivoHelper.DetectarTipo</c>). Son dos pruebas distintas y complementarias:
/// esta evita el falso rechazo de un archivo legítimo, y la del servicio es la que frena el
/// contenido realmente malicioso (RNF-009).
/// </para>
/// <para>
/// <b>Todas las validaciones lanzan <see cref="ValidacionException"/></b> (→ 422) con un mensaje
/// <b>explícito y accionable</b>: el actor necesita saber qué corregir. <c>NotFoundException</c>
/// (404) y <c>AccesoDenegadoException</c> (403) NO salen de esta clase: son del servicio, porque
/// dependen de la lectura de la acción, del ciclo y del actor.
/// </para>
/// </summary>
public sealed class EntregableAdjuntoReglasNegocio
{
    /// <summary>Único estado de ciclo que admite escritura (F3 / RC-12).</summary>
    private const string EstadoCicloActivo = "Activo";

    /// <summary>Tope de la parte entera en MB, solo para los mensajes de error (D-L: la constante manda).</summary>
    private const long BytesPorMegabyte = 1024 * 1024;

    private readonly TimeProvider _time;

    /// <summary>
    /// <see cref="TimeProvider"/> inyectado, no <c>DateTime.Now</c> (spec § Validaciones): es la
    /// única dependencia del entorno, de modo que en cuanto exista una regla que dependa del
    /// instante (caducidad, ventanas) tendrá que ser fijable por un test. Hoy ninguna de las 4
    /// reglas lo consume, pero <b>el constructor queda fijado desde la fase roja</b> para que
    /// añadirla después no rompa ningún test.
    /// </summary>
    public EntregableAdjuntoReglasNegocio(TimeProvider time) => _time = time;

    /// <summary>
    /// Fuente de tiempo de las reglas, expuesta como costura de test. Mismo criterio que el resto
    /// del repositorio: el entorno entra por inyección, nunca por una llamada estática al reloj.
    /// </summary>
    public TimeProvider Time => _time;

    /// <summary>
    /// CA #1 (RN-020) — máximo de 5 adjuntos por acción. Se valida <b>en dos niveles</b>, porque
    /// son dos fallos distintos con dos mensajes distintos:
    /// <list type="number">
    ///   <item>La <b>petición</b> debe traer entre 1 y 5 archivos: 0 o más de 5 se rechaza
    ///   aunque el total de la acción quepa.</item>
    ///   <item>El <b>total</b> (los que ya tiene la acción + los que llegan) no puede pasar de 5.
    ///   El conteo lo calcula la BLL sobre las filas existentes, porque la tabla no tiene contador
    ///   ni trigger (DB-04).</item>
    /// </list>
    /// El mensaje del segundo caso nombra los existentes, el tope y los recibidos: un «demasiados
    /// archivos» genérico no le dice al usuario qué tiene que quitar.
    /// </summary>
    public void ValidarConteo(int existentes, int nuevos)
    {
        if (nuevos <= 0 || nuevos > EntregableAdjuntoReglas.MaximoArchivosPorAccion)
            throw new ValidacionException(
                $"Debe enviar entre 1 y {EntregableAdjuntoReglas.MaximoArchivosPorAccion} archivos. Recibidos: {nuevos}.");

        if (existentes + nuevos > EntregableAdjuntoReglas.MaximoArchivosPorAccion)
            throw new ValidacionException(
                $"La acción ya tiene {existentes} adjunto(s) y no admite más de {EntregableAdjuntoReglas.MaximoArchivosPorAccion}. Recibidos: {nuevos}.");
    }

    /// <summary>
    /// RN-020 — tamaño por archivo: mayor que 0 y como mucho 20 MB, con independencia del tamaño
    /// del lote. El mensaje identifica el archivo culpable, porque el lote puede venir de 5.
    /// </summary>
    public void ValidarTamano(long tamanoBytes, string nombreArchivo)
    {
        var nombre = (nombreArchivo ?? string.Empty).Trim();

        if (tamanoBytes <= 0)
            throw new ValidacionException($"El archivo «{nombre}» está vacío o tiene un tamaño no válido.");

        if (tamanoBytes > EntregableAdjuntoReglas.TamanoMaximoBytes)
            throw new ValidacionException(
                $"El archivo «{nombre}» supera el máximo de {EntregableAdjuntoReglas.TamanoMaximoBytes / BytesPorMegabyte} MB por archivo.");
    }

    /// <summary>
    /// CA #2 (RN-020) — allowlist de 5 tipos (PDF, DOCX, XLSX, PNG, JPG) por <b>extensión Y
    /// MIME</b>, con la tolerancia justificada de D-C: Chrome y Windows envían
    /// <c>application/octet-stream</c> o <c>application/zip</c> en archivos legítimos, así que se
    /// aceptan cuando la extensión sí es válida (el tipo canónico lo fija después la firma de
    /// bytes, que no se relaja por tolerar el alias).
    /// <para>
    /// Rechaza también el <b>spoofing</b> de pareja extensión/MIME (<c>.pdf</c> declarado con
    /// <c>image/png</c>): ambos valores por separado serían legales de la allowlist, pero juntos
    /// describen archivos distintos.
    /// </para>
    /// </summary>
    public void ValidarTipo(string extension, string contentType, string nombreArchivo)
    {
        var nombre = (nombreArchivo ?? string.Empty).Trim();
        var ext = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        var mime = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (!TipoArchivoHelper.ExtensionesPermitidas.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new ValidacionException(
                $"El tipo de archivo «{nombre}» (extensión .{ext}) no está permitido. Permitidos: PDF, DOCX, XLSX, PNG, JPG.");

        var esMimeGenericoTolerado = TipoArchivoHelper.MimesGenericosTolerados.Contains(mime, StringComparer.OrdinalIgnoreCase);
        if (!esMimeGenericoTolerado && !TipoArchivoHelper.MimesPermitidos.Contains(mime, StringComparer.OrdinalIgnoreCase))
            throw new ValidacionException(
                $"El tipo MIME «{mime}» del archivo «{nombre}» no está permitido. Permitidos: PDF, DOCX, XLSX, PNG, JPG.");

        if (esMimeGenericoTolerado) return;

        // La pareja debe ser coherente: el MIME canónico de la extensión declarada.
        var mimeEsperado = TipoArchivoHelper.MimeCanonica(TipoDesdeExtension(ext));
        if (!string.Equals(mime, mimeEsperado, StringComparison.OrdinalIgnoreCase))
            throw new ValidacionException(
                $"El tipo MIME «{mime}» no corresponde a la extensión «.{ext}» del archivo «{nombre}».");
    }

    /// <summary>
    /// F3 / RC-12 — los adjuntos solo se escriben en ciclo <b>Activo</b>. <c>Cerrado</c> y
    /// <c>Borrador</c> son de solo lectura → 422. El mensaje nombra el estado para que la UI pueda
    /// explicarlo, en vez de un «operación no permitida» sin contexto.
    /// </summary>
    public void ValidarCicloCerrado(string estadoCiclo)
    {
        var estado = (estadoCiclo ?? string.Empty).Trim();
        if (string.Equals(estado, EstadoCicloActivo, StringComparison.OrdinalIgnoreCase)) return;

        throw new ValidacionException(
            $"El ciclo está en estado «{estado}». Solo se pueden gestionar entregables cuando el ciclo está {EstadoCicloActivo}.");
    }

    /// <summary>Tipo canónico que corresponde a una extensión de la allowlist (CA #2).</summary>
    private static TipoArchivo TipoDesdeExtension(string extension) => extension switch
    {
        "pdf" => TipoArchivo.Pdf,
        "docx" => TipoArchivo.Docx,
        "xlsx" => TipoArchivo.Xlsx,
        "png" => TipoArchivo.Png,
        "jpg" or "jpeg" => TipoArchivo.Jpg,
        _ => TipoArchivo.Desconocido
    };
}
