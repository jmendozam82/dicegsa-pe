using System.Globalization;
using System.Text;

namespace PE_GOL.Utility.Helpers;

/// <summary>
/// Constantes y formato de los entregables adjuntos — Spec HU-022 (ARCH-06, RN-020, CA #1/#2).
/// <para>
/// <b>Este helper NO valida nada</b> (no comprueba allowlists, tamaños ni conteos): es un
/// <b>constante + formato</b>. Las reglas de negocio viven exclusivamente en
/// <c>PE-GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs</c> (§ Validaciones del spec),
/// con el mismo criterio de separación que <c>PlanLimitValidator</c>: <c>PE-GOL.Utility</c> no
/// puede referenciar <c>PE-GOL.BLL</c> (ARCH-02) y las allowlists necesitan
/// <c>TimeProvider</c> y <c>ValidacionException</c> de la BLL.
/// </para>
/// <para>
/// <b>Por qué son constantes y no literales sueltos (D-L):</b> los umbrales <c>5</c>,
/// <c>20 MB</c> y <c>255</c> los consumen la BLL, el <c>AbstractValidator</c> de la API y la
/// vista Razor. Cambiar RN-020 se hace en un único sitio.
/// </para>
/// </summary>
public static class EntregableAdjuntoReglas
{
    /// <summary>ARCH-06 — bucket privado de Supabase Storage. Nunca lo elige el cliente (SEC-06).</summary>
    public const string BucketEntregables = "entregables";

    /// <summary>CA #1 — máximo de adjuntos por acción (contado en BLL sobre las filas existentes, DB-04).</summary>
    public const int MaximoArchivosPorAccion = 5;

    /// <summary>RN-020 — 20 MiB por archivo.</summary>
    public const long TamanoMaximoBytes = 20L * 1024 * 1024;

    /// <summary>ARCH-06 / RNF-008 — expiración de la URL firmada, en horas.</summary>
    public const int ExpiracionHoras = 24;

    /// <summary>Longitud de la columna <c>nombre_archivo</c> (varchar(255) del DDL).</summary>
    public const int LongitudMaximaNombre = 255;

    private const string TamanoPorDefecto = "adjunto";

    /// <summary>
    /// Nombres base reservados por Windows: un tenant served sobre Windows no podría persistirlos.
    /// Se prefijan con guion bajo, lo que además mantiene la extensión.
    /// </summary>
    private static readonly HashSet<string> NombresReservadosWindows = new(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Tamaño formateado para la vista, con coma decimal (coherente con el resto de la app) y
    /// base 1024: <c>"0 B"</c>, <c>"512 B"</c>, <c>"1,0 KB"</c>, <c>"1,4 MB"</c>, <c>"20,0 MB"</c>.
    /// <para>
    /// Se formatea con <see cref="CultureInfo.InvariantCulture"/> y se sustituye el punto por
    /// coma de forma explícita, para que la salida NO dependa de la configuración regional de la
    /// máquina (los tests la afirman con literales).
    /// </para>
    /// </summary>
    public static string FormatearTamano(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";

        double valor = bytes;
        string unidad;

        if (bytes < 1024L * 1024)
        {
            valor = bytes / 1024d;
            unidad = "KB";
        }
        else if (bytes < 1024L * 1024 * 1024)
        {
            valor = bytes / (1024d * 1024);
            unidad = "MB";
        }
        else
        {
            valor = bytes / (1024d * 1024 * 1024);
            unidad = "GB";
        }

        var redondeado = Math.Round(valor, 1, MidpointRounding.AwayFromZero)
            .ToString("0.0", CultureInfo.InvariantCulture)
            .Replace('.', ',');

        return $"{redondeado} {unidad}";
    }

    /// <summary>
    /// Nombre legible y seguro que se persiste en <c>nombre_archivo</c> (D-E, RNF-009).
    /// <para>
    /// <b>Nunca se usa para construir la ruta de Storage</b> —esa va con UUID (ARCH-06)—: solo
    /// para lo que el usuario ve. Pasos, en orden:
    /// (1) descarta cualquier ruta (<c>Path.GetFileName</c>, separadores <c>/</c> y <c>\</c>);
    /// (2) quita caracteres de control y los reservados de Windows <c>&lt; &gt; : " / \ | ? *</c>;
    /// (3) colapsa espacios; (4) recorta a <see cref="LongitudMaximaNombre"/> por la
    /// <b>extensión primero</b> (se conserva la extensión, se recorta la base: el usuario
    /// reconoce su archivo); (5) desambigua nombres base reservados de Windows; (6) si queda
    /// vacío devuelve <c>"adjunto"</c>, porque la columna es NOT NULL y la vista la pinta.
    /// </para>
    /// </summary>
    public static string SanearNombreArchivo(string nombreOriginal)
    {
        var nombre = QuitarRuta(nombreOriginal);
        nombre = QuitarCaracteresProhibidos(nombre);
        nombre = ColapsarEspacios(nombre);

        if (nombre.Length > LongitudMaximaNombre)
            nombre = RecortarConservandoExtension(nombre, LongitudMaximaNombre);

        if (NombresReservadosWindows.Contains(Path.GetFileNameWithoutExtension(nombre).ToUpperInvariant()))
            nombre = "_" + nombre;

        return string.IsNullOrWhiteSpace(nombre) ? TamanoPorDefecto : nombre;
    }

    /// <summary>Descarta cualquier ruta: el nombre visible nunca es una ruta (D-E, path traversal).</summary>
    private static string QuitarRuta(string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return string.Empty;

        // Se hace a mano en lugar de Path.GetFileName porque este último solo reconoce '\\' como
        // separador en Windows: hacerlo aquí mantiene el comportamiento idéntico en cualquier SO.
        var corte = nombre.LastIndexOfAny(new[] { '/', '\\' });
        return corte >= 0 ? nombre[(corte + 1)..] : nombre;
    }

    /// <summary>Quita caracteres de control y los reservados por Windows (RNF-009: nada de XSS).</summary>
    private static string QuitarCaracteresProhibidos(string nombre)
    {
        var sb = new StringBuilder(nombre.Length);
        foreach (var c in nombre)
        {
            if (char.IsControl(c)) continue;
            if (c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Colapsa rachas de espacios a una sola espacio y quita los sobrantes de los bordes.</summary>
    private static string ColapsarEspacios(string nombre)
    {
        var sb = new StringBuilder(nombre.Length);
        var espacioPendiente = false;

        foreach (var c in nombre)
        {
            if (char.IsWhiteSpace(c))
            {
                espacioPendiente = sb.Length > 0;
                continue;
            }
            if (espacioPendiente) sb.Append(' ');
            sb.Append(c);
            espacioPendiente = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Recorta a <paramref name="limite"/> caracteres recortando la <b>base</b>, nunca la
    /// extensión: es lo que permite que el usuario siga reconociendo el archivo.
    /// </summary>
    private static string RecortarConservandoExtension(string nombre, int limite)
    {
        var extension = Path.GetExtension(nombre);
        if (extension.Length >= limite)
        {
            // Extensión patológica (más larga que el propio límite): no hay base que conservar.
            return extension[..limite];
        }

        var baseRecortada = nombre[..(nombre.Length - extension.Length)];
        var maximoBase = limite - extension.Length;
        if (baseRecortada.Length > maximoBase)
            baseRecortada = baseRecortada[..maximoBase];

        return baseRecortada + extension;
    }
}
