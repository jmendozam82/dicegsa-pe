using System.IO.Compression;
using System.Text;
using Moq;
using PE_GOL.BLL.Limites;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Helpers;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-022 — <b>reglas y helpers de dominio</b>. Escritos ANTES de la
/// implementación (TEST-01): <b>el rojo legítimo es el fallo de compilación</b> (no existen
/// <c>PE_GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs</c>, ni
/// <c>PE_GOL.Utility/Files/TipoArchivoHelper.cs</c>, ni <c>PE_GOL.Utility/Helpers/EntregableAdjuntoReglas.cs</c>).
/// Misma convención que AccionPlanGanttServiceTests (HU-021).
/// <para>
/// <b>Nombre del archivo.</b> El encargo pidió <c>EntregableAdjuntoReglasNegocioTests.cs</c> y se
/// respeta ese nombre. Conviene saber, sin embargo, qué hay realmente debajo, porque el spec
/// reparte estas pruebas entre <b>dos capas distintas</b> y conviene no confundirlas:
/// </para>
/// <list type="bullet">
///   <item>Las <b>4 validaciones</b> (<c>ValidarConteo</c>, <c>ValidarTamano</c>,
///     <c>ValidarTipo</c>, <c>ValidarCicloCerrado</c>) ejercitan
///     <c>PE_GOL.BLL/Limites/EntregableAdjuntoReglasNegocio</c> —código puro, sin FluentValidation
///     (corrección 5 / D-N)— y <b>SÍ computan para TEST-02</b>.</item>
///   <item>Los <b>4 casos 62-65</b> del spec ejercitan las 2 clases estáticas de
///     <c>PE-GOL.Utility</c> (<c>TipoArchivoHelper</c> y <c>EntregableAdjuntoReglas</c>), que
///     <b>NO</b> computan para TEST-02 (que mide <c>PE-GOL.BLL</c>).</item>
/// </list>
/// <para>
/// La separación no es un capricho: <c>PE-GOL.Utility</c> no puede referenciar a
/// <c>PE-GOL.BLL</c> (ARCH-02), y las allowlists necesitan <c>TimeProvider</c> y
/// <c>ValidacionException</c> de la BLL. Lo que puede vivir en Utility son constantes y formato.
/// </para>
/// <para>
/// <b>CONTRATO QUE @BackendDev DEBE IMPLEMENTAR</b> (sin esto el archivo no compila):
/// <code>
/// // PE-GOL.Utility/Helpers/EntregableAdjuntoReglas.cs
/// namespace PE_GOL.Utility.Helpers;
/// public static class EntregableAdjuntoReglas
/// {
///     public const string BucketEntregables = "entregables";
///     public const int    MaximoArchivosPorAccion = 5;
///     public const long   TamanoMaximoBytes = 20L * 1024 * 1024;
///     public const int    ExpiracionHoras = 24;
///     public const int    LongitudMaximaNombre = 255;
///     public static string FormatearTamano(long bytes);
///     public static string SanearNombreArchivo(string nombreOriginal);
/// }
///
/// // PE-GOL.Utility/Files/TipoArchivoHelper.cs   ← NUEVO (nota «a» del encabezado v2)
/// namespace PE_GOL.Utility.Files;
/// public enum TipoArchivo { Pdf, Docx, Xlsx, Png, Jpg, Desconocido }   // enum CERRADO (bloqueo 4)
/// public static class TipoArchivoHelper
/// {
///     public static readonly IReadOnlyList&lt;string&gt; ExtensionesPermitidas;      // pdf docx xlsx png jpg
///     public static readonly IReadOnlyList&lt;string&gt; MimesPermitidos;
///     public static readonly IReadOnlyList&lt;string&gt; MimesGenericosTolerados;    // octet-stream, zip
///     static TipoArchivo    DetectarTipo(ReadOnlySpan&lt;byte&gt; cabecera);
///     static string         ContentDisposition(TipoArchivo tipo);                  // «inline» | «attachment»
///     static string         ExtensionCanonica(string extension, string contentType);
/// }
///
/// // PE-GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs
/// namespace PE_GOL.BLL.Limites;                     // mismo estilo que PlanLimitValidator
/// public class EntregableAdjuntoReglasNegocio        // clase NORMAL, sin heredar nada
/// {
///     public EntregableAdjuntoReglasNegocio(TimeProvider time);
///     public void ValidarConteo(int existentes, int nuevos);
///     public void ValidarTamano(long tamanoBytes, string nombreArchivo);
///     public void ValidarTipo(string extension, string contentType, string nombreArchivo);
///     public void ValidarCicloCerrado(string estadoCiclo);
/// }
/// </code>
/// <b>Por qué <c>TimeProvider</c> inyectado y no <c>DateTime.Now</c></b> (spec § Validaciones):
/// es la única dependencia del entorno, y en cuanto exista la caducidad de URLs firmadas tendrá
/// que ser fijable por un test. Mockearla aquí, aunque hoy ningún método la use, fija el
/// constructor desde la fase roja.
/// <para>
/// <b>Todas las validaciones lanzan <c>ValidacionException</c></b> (→ 422) con el mensaje EXACTO de
/// cada tabla del spec. <c>NoEncontradoException</c> (404) se lanza en el servicio, nunca aquí;
/// <c>AccesoDenegadoException</c> (403) tampoco sale de esta clase.
/// </para>
/// </summary>
public class EntregableAdjuntoReglasNegocioTests
{
    private readonly TimeProvider _time = Mock.Of<TimeProvider>();
    private EntregableAdjuntoReglasNegocio _sut = null!;

    public EntregableAdjuntoReglasNegocioTests() => _sut = new EntregableAdjuntoReglasNegocio(_time);

    // ═════════════════════════════ ValidarConteo · CA #1 ══════════════════════════════════

    /// <summary>
    /// Los 2 bordes de «1..5 archivos por petición» (RN-020 / CA #1). <c>0</c> y <c>6</c> son
    /// <b>422</b>: una petición vacía o por encima del tope no llega ni a tocar Storage.
    /// </summary>
    [Theory]
    [InlineData(0)]   // 0 archivos → 422 (una petición sin archivos no sube nada)
    [InlineData(6)]   // 6 archivos → 422, aunque individualmente sean válidos
    public void ValidarConteo_CantidadFueraDeUnoACinco_LanzaValidacionException(int nuevos)
    {
        // Arrange: 0 existentes (la petición no se valida contra el total aún)
        const int existentes = 0;

        // Act + Assert
        var ex = Assert.Throws<ValidacionException>(() => _sut.ValidarConteo(existentes, nuevos));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    /// <summary>
    /// Los 5 valores válidos del rango 1..5, <b>con el total de la acción dentro del tope</b>.
    /// Que estos 5 NO lancen es lo que permite a los casos 3 y 6 de la BLL llegar a 201.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]   // 1 nuevo, acción vacía
    [InlineData(2, 3)]   // 2 nuevos sobre 3 existentes = 5 (borde exacto del total)
    [InlineData(5, 0)]   // 5 nuevos, el máximo por petición
    [InlineData(1, 4)]   // 1 nuevo sobre 4 existentes = 5 (borde, caso 6 de la BLL)
    public void ValidarConteo_CantidadValida_NoLanzaException(int nuevos, int existentes)
    {
        // Act
        var ex = Record.Exception(() => _sut.ValidarConteo(existentes, nuevos));

        // Assert
        Assert.Null(ex);
    }

    /// <summary>
    /// El borde que el caso 5 de la BLL ejerce end-to-end: la acción ya tiene 5 y llega 1 más.
    /// El mensaje debe ser <b>explícito y accionable</b> («la acción ya tiene N adjunto(s)…»), no
    /// un genérico «demasiados archivos» (spec § paso 5).
    /// </summary>
    [Fact]
    public void ValidarConteo_AccionYaEnElTopeYMandaMas_LanzaValidacionExceptionYLoDice()
    {
        // Arrange
        const int existentes = 5;
        const int nuevos = 1;

        // Act
        var ex = Assert.Throws<ValidacionException>(() => _sut.ValidarConteo(existentes, nuevos));

        // Assert: el mensaje nombra los 5 existentes y el máximo, para que el usuario sepa qué hacer
        Assert.Contains("5", ex.Message, StringComparison.Ordinal);
        Assert.Contains(EntregableAdjuntoReglas.MaximoArchivosPorAccion.ToString(), ex.Message, StringComparison.Ordinal);
    }

    // ═════════════════════════════ ValidarTamano · RN-020 ══════════════════════════════════

    /// <summary>
    /// RN-020: > 0 y ≤ 20 MB, <b>por archivo</b> (independiente del tamaño del lote). El tope sale
    /// de la constante, no de un literal (D-L): cambiar RN-020 se hace en un único sitio.
    /// </summary>
    [Theory]
    [InlineData(0L)]                    // archivo vacío → 422 (el caso 4 valida > 0)
    [InlineData(-1L)]                   // tamaño negativo → 422 (oráculo hostil)
    [InlineData(20L * 1024 * 1024 + 1)] // 20 MiB + 1 byte → 422 (el caso 7)
    public void ValidarTamano_TamanoFueraDeRango_LanzaValidacionException(long tamanoBytes)
    {
        // Act + Assert
        var ex = Assert.Throws<ValidacionException>(
            () => _sut.ValidarTamano(tamanoBytes, "informe.pdf"));

        // Assert: el mensaje identifica el archivo culpable (el lote puede venir de 5)
        Assert.Contains("informe.pdf", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El borde permitido por el caso 8: <b>exactamente</b> 20 MiB entra. Y el borde inferior, 1
    /// byte, también (un archivo mínimo pero real).
    /// </summary>
    [Theory]
    [InlineData(1L)]                      // 1 byte
    [InlineData(1_048_576L)]              // 1 MiB
    [InlineData(20L * 1024 * 1024)]       // el tope EXACTO → 201 (caso 8)
    public void ValidarTamano_TamanoValido_NoLanzaException(long tamanoBytes)
    {
        // Act
        var ex = Record.Exception(() => _sut.ValidarTamano(tamanoBytes, "informe.pdf"));

        // Assert
        Assert.Null(ex);
    }

    // ═══════════════════════ ValidarTipo · CA #2 / RNF-009 (allowlist) ══════════════════════

    /// <summary>
    /// Los 5 tipos de la allowlist (CA #2) con su MIME canónico: ninguno lanza. Es la misma
    /// allowlist que consumen el <c>AbstractValidator</c> de la API y <c>TipoArchivoHelper</c>; la
    /// BLL es la fuente de verdad (UX-04), por eso se repite aquí.
    /// </summary>
    [Theory]
    [InlineData("pdf",  "application/pdf")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("png",  "image/png")]
    [InlineData("jpg",  "image/jpeg")]
    public void ValidarTipo_ExtensionYMimeEnAllowlist_NoLanzaException(string extension, string contentType)
    {
        // Act
        var ex = Record.Exception(() => _sut.ValidarTipo(extension, contentType, "archivo." + extension));

        // Assert
        Assert.Null(ex);
    }

    /// <summary>
    /// Las <b>2 tolerancias justificadas de D-C</b>: Chrome y Windows envían
    /// <c>application/octet-stream</c> o <c>application/zip</c> en archivos legítimos. La BLL los
    /// acepta (el tipo canónico lo fija después la firma de bytes, paso 11.b) — rechazarlos sería
    /// un falso negativo de UX.
    /// </summary>
    [Theory]
    [InlineData("pdf",  "application/octet-stream")]   // el caso 10 de la BLL
    [InlineData("docx", "application/zip")]
    [InlineData("xlsx", "application/octet-stream")]
    public void ValidarTipo_MimeGenericoToleradoConExtensionValida_NoLanzaException(
        string extension, string contentType)
    {
        // Act
        var ex = Record.Exception(() => _sut.ValidarTipo(extension, contentType, "archivo." + extension));

        // Assert
        Assert.Null(ex);
    }

    /// <summary>
    /// RNF-009 / XSS: lo que la allowlist debe <b>rechazar</b>. El <c>.exe</c> del caso 9, una
    /// extensión vacía y el spoofing «extensión válida + MIME de otro tipo». Nótese que
    /// <c>ValidarTipo</c> NO puede detectar un PDF renombrado: de eso se encarga la firma de
    /// bytes (<c>TipoArchivoHelper.DetectarTipo</c>, caso 62), que es una prueba distinta y complementaria.
    /// </summary>
    [Theory]
    [InlineData("exe",  "application/octet-stream")]        // ejecutable → 422 (caso 9)
    [InlineData("",     "application/pdf")]                 // sin extensión → 422
    [InlineData("pdf",  "image/png")]                       // spoofing extensión/MIME → 422
    [InlineData("pdf",  "text/html")]                       // HTML con nombre de PDF → 422
    public void ValidarTipo_FueraDeAllowlist_LanzaValidacionException(string extension, string contentType)
    {
        // Act + Assert
        var ex = Assert.Throws<ValidacionException>(
            () => _sut.ValidarTipo(extension, contentType, "sospechoso.bin"));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    // ═══════════════ ValidarCicloCerrado · F3 / RC-12 (ciclo «Activo») ═════════════════════

    /// <summary>
    /// F3 / RC-12: los adjuntos solo se escriben en ciclo <b>Activo</b>. <c>Cerrado</c> (casos 17 y
    /// 48) y <c>Borrador</c> (caso 18) son de solo lectura → 422. Coherente con
    /// <c>AccionService.ActualizarProgresoAsync</c> y con <c>DetalleAccion.cshtml</c>, que ya
    /// deshabilita la edición en ciclos cerrados.
    /// </summary>
    [Theory]
    [InlineData("Cerrado")]
    [InlineData("Borrador")]
    public void ValidarCicloCerrado_CicloNoActivo_LanzaValidacionException(string estadoCiclo)
    {
        // Act + Assert
        var ex = Assert.Throws<ValidacionException>(() => _sut.ValidarCicloCerrado(estadoCiclo));

        // Assert: el mensaje nombra el estado, para que la UI pueda explicar por qué
        Assert.Contains(estadoCiclo, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El único estado que permite escribir: <c>Activo</c>.</summary>
    [Fact]
    public void ValidarCicloCerrado_CicloActivo_NoLanzaException()
    {
        // Act
        var ex = Record.Exception(() => _sut.ValidarCicloCerrado("Activo"));

        // Assert
        Assert.Null(ex);
    }

    // ═════════════════════ 62 · TipoArchivoHelper.DetectarTipo (5 cabeceras) ═══════════════

    [Theory]
    // PDF: %PDF-1.7 → Pdf
    [InlineData(TipoArchivo.Pdf)]
    // ZIP con word/ → Docx (un DOCX renombrado a .xlsx se delata por su directorio central)
    [InlineData(TipoArchivo.Docx)]
    // ZIP con xl/ → Xlsx
    [InlineData(TipoArchivo.Xlsx)]
    // PNG: 89 50 4E 47 0D 0A 1A 0A → Png
    [InlineData(TipoArchivo.Png)]
    // JPG: FF D8 FF E0 → Jpg
    [InlineData(TipoArchivo.Jpg)]
    public void TipoArchivoHelper_DetectarTipo_CabecerasValidas_DevuelveElTipoCanonico(TipoArchivo esperado)
    {
        // Arrange: el prefijo en bytes de cada uno de los 5 tipos permitidos (CA #2)
        var cabecera = esperado switch
        {
            TipoArchivo.Pdf => new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25 },
            TipoArchivo.Png => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 },
            TipoArchivo.Jpg => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 },
            TipoArchivo.Docx => CrearZipCon("word/document.xml"),
            TipoArchivo.Xlsx => CrearZipCon("xl/workbook.xml", "[Content_Types].xml"),
            _ => throw new ArgumentOutOfRangeException(nameof(esperado), esperado, "Tipo no contemplado.")
        };

        // Act
        var detectado = TipoArchivoHelper.DetectarTipo(cabecera);

        // Assert: el tipo canónico, NO el del nombre del archivo (el nombre es del cliente: RNF-009)
        Assert.Equal(esperado, detectado);
    }

    [Fact]
    public void TipoArchivoHelper_DetectarTipo_CabeceraDesconocida_DevuelveDesconocido()
    {
        // Arrange: 0x00 0x01 0x02 no es ninguno de los 5 tipos permitidos

        // Act
        var detectado = TipoArchivoHelper.DetectarTipo(new byte[] { 0x00, 0x01, 0x02 });

        // Assert: Desconocido → la BLL lo rechaza con 422; no se "adivina" por extensión
        Assert.Equal(TipoArchivo.Desconocido, detectado);
    }

    /// <summary>
    /// El <c>content-disposition</c> de ADR-013 (bloqueo 8): PDF se sirve <b>inline</b> (que el
    /// visor del navegador abra el entregable) y el resto como <b>attachment</b>. Sin
    /// <c>filename=</c>: el nombre visible es la columna <c>nombre_archivo</c> y la ruta lleva UUID.
    /// </summary>
    [Fact]
    public void TipoArchivoHelper_ContentDisposition_PdfInlineYRestoAttachment()
    {
        Assert.Equal("inline", TipoArchivoHelper.ContentDisposition(TipoArchivo.Pdf));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Docx));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Xlsx));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Png));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Jpg));
        // Desconocido nunca llega al Storage (se rechaza en el paso 11.b), pero si llegara, no se sirve inline
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Desconocido));
    }

    // ═════════════════════════ 64 · EntregableAdjuntoReglas.FormatearTamano ════════════════

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1_024L, "1,0 KB")]            // borde: 1 KB exacto
    [InlineData(1_048_576L, "1,0 MB")]       // borde: 1 MB exacto
    [InlineData(20_971_520L, "20,0 MB")]     // borde: el tope de RN-020 (20 MiB)
    public void EntregableAdjuntoReglas_FormatearTamano_TamanosRepresentativos_DevuelveLaUnidadConComaDecimal(
        long tamano, string esperado)
    {
        // Act
        var texto = EntregableAdjuntoReglas.FormatearTamano(tamano);

        // Assert: coma decimal (coherente con el resto de la app) y cambio de unidad en 1024
        Assert.Equal(esperado, texto);
    }

    // ═════════════════ 65 · EntregableAdjuntoReglas.SanearNombreArchivo ═══════════════════

    /// <summary>Nombres reservados por Windows: un tenant sobre Windows no podría persistirlos.</summary>
    private static readonly string[] ReservadosWindows =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Los 5 datos hostiles del caso 65 (spec) y la extensión que debe sobrevivir al saneado
    /// (<c>null</c> cuando la entrada no trae ninguna que conservar).
    /// </summary>
    public static TheoryData<string, string?> CasosDeSaneo => new()
    {
        { @"..\..\evil<script>.pdf", "pdf" },              // ruta + XSS (path traversal)
        { "CON.pdf", "pdf" },                               // nombre reservado de Windows
        { "informe [31m.pdf", "pdf" },                      // ESC colado (carácter de control)
        { "", null },                                       // vacío → "adjunto"
        { new string('a', 400) + ".pdf", "pdf" }            // no cabe en el varchar(255) del DDL
    };

    [Theory]
    [MemberData(nameof(CasosDeSaneo))]
    public void EntregableAdjuntoReglas_SanearNombre_EntradasHostiles_DevuelveNombreSeguroYRespetandoElLimite(
        string nombreOriginal, string? extensionEsperada)
    {
        // Act
        var saneado = EntregableAdjuntoReglas.SanearNombreArchivo(nombreOriginal);

        // Assert: los 5 invariantes que fija el caso 65 (spec), para TODAS las entradas.

        // 1 · nunca vacío (nombre_archivo es NOT NULL y la UI lo pinta en la tabla)
        Assert.False(string.IsNullOrWhiteSpace(saneado));

        // 2 · sin ruta ni '..' (D-E: el nombre visible nunca es una ruta)
        Assert.DoesNotContain("..", saneado, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", saneado, StringComparison.Ordinal);
        Assert.DoesNotContain("/", saneado, StringComparison.Ordinal);

        // 3 · sin caracteres de control ni de XSS (RNF-009)
        Assert.DoesNotContain("<", saneado, StringComparison.Ordinal);
        Assert.DoesNotContain(">", saneado, StringComparison.Ordinal);
        Assert.All(saneado, c => Assert.False(char.IsControl(c),
            $"Quedó el carácter de control U+{(int)c:X4} en «{saneado}»."));

        // 4 · sin nombre reservado de Windows
        var sinExtension = Path.GetFileNameWithoutExtension(saneado).ToUpperInvariant();
        Assert.DoesNotContain(ReservadosWindows, r => sinExtension == r);

        // 5 · ≤ 255 caracteres y, si la entrada traía extensión, esta se conserva: lo que se
        //     recorta es la BASE, nunca el sufijo (el usuario reconoce su archivo)
        Assert.True(saneado.Length <= EntregableAdjuntoReglas.LongitudMaximaNombre,
            $"«{saneado}» mide {saneado.Length} caracteres y el máximo es {EntregableAdjuntoReglas.LongitudMaximaNombre}.");
        if (extensionEsperada is not null)
            Assert.Equal(extensionEsperada, Path.GetExtension(saneado).TrimStart('.').ToLowerInvariant());
    }

    // ══════════════════════ HELPERS DE CONSTRUCCIÓN ═════════════════════════════════════════

    /// <summary>
    /// ZIP real con las entradas indicadas. Sirve para los 2 tipos de Office (DOCX y XLSX), que
    /// comparten la firma <c>PK\x03\x04</c>: lo que los DISTINGUE es el directorio central del
    /// paquete (<c>word/</c> frente a <c>xl/</c>), y eso es justo lo que alguien falsearía al
    /// renombrar un .docx a .xlsx — de ahí el caso 12 de la BLL.
    /// </summary>
    private static byte[] CrearZipCon(params string[] entradas)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var nombre in entradas)
            {
                using var escritura = new StreamWriter(zip.CreateEntry(nombre).Open(), Encoding.UTF8);
                escritura.Write("<contenido/>");
            }
        }
        return ms.ToArray();
    }
}
