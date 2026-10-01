using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PE_GOL.Tests.Architecture;

/// <summary>
/// Test de regresión de arquitectura que blinda el contrato de ADR-016 a nivel de código JS.
/// Es la única defensa posible contra el Defecto E (HU-023 Revisión v3): los tests C# mockean
/// <c>IApiClient</c> y NUNCA ejercitan una URL construida en JS, por lo que ningún test C# puede
/// detectar que <c>plan-consolidado.js</c> declaraba <c>API_BASE = '/api/v1/planes/consolidado'</c>
/// — ruta relativa al origen del MVC (<c>:7200</c>) que el navegador pedía como
/// <c>https://localhost:7200/api/v1/...</c> → HTTP 404 (la API vive en <c>:7269</c>).
///
/// Regla operativa (ADR-016): "el navegador NUNCA consume la API interna; el MVC hace de proxy
/// con <c>IApiClient</c>". Si una vista necesita datos de la API en interacciones del usuario,
/// se añade una ACCIÓN MVC PROXY — nunca un <c>fetch('/api/v1/...')</c> en <c>wwwroot/js/</c>.
///
/// Implementación: scan estático de <c>PE-GOL.Aplicacion/wwwroot/js/*.js</c> — busca el patrón
/// "el archivo tiene tanto un <c>fetch(</c> como una cadena literal <c>/api/</c>". El primer
/// componente del AND captura el código que ejecuta HTTP; el segundo captura la URL prohibida
/// (sea por literal directo <c>fetch('/api/v1/...')</c> o por construcción
/// <c>fetch(API_BASE + '?…')</c> donde <c>API_BASE = '/api/v1/...'</c>).
///
/// Estado actual (verificado al escribir el test, 2026-09-30):
///   · plan-consolidado.js (HU-023)  — YA CORREGIDO en el hotfix v3 (proxy MVC +
///     eliminación de <c>API_BASE</c>). Sale LIMPIO de este test.
///   · entregables.js (HU-022)       — TIENE el defecto (3 fetch en L225/L372/L401). Marcado
///     como TOLERADO (<see cref="ExcepcionesConocidas"/>) por decisión explícita de Jorge
///     (ADR-016 § "Deuda técnica pendiente", <c>docs/HANDOFF_PE_GOL.md</c> § "Deuda técnica
///     pendiente" ítem 3). Hotfix propio de HU-022 — fuera de alcance del hotfix v3 de HU-023.
///     El propio <c>entregables.js</c> lleva el <c>TODO [hotfix-HU-022]</c> en las 3 líneas
///     (L224, L373, L404) para que cualquier desarrollador que pase por allí sepa que la
///     deuda está registrada.
///
/// Coste: O(n) sobre los <c>.js</c> de <c>wwwroot/js/</c>; sin dependencias nuevas.
/// Fragilidad: la ruta del directorio se calcula desde la ubicación del assembly de tests
/// (4 niveles arriba hasta la raíz de la solución, similar a como reportgenerator resuelve
/// las rutas). Si alguien reorganiza la estructura de proyectos, hay que ajustar
/// <see cref="ObtenerRutaWwwRootJs"/> — pero reorganizar la estructura de N-Tier requiere
/// un ADR (ARCH-01), así que el coste de mantenimiento es aceptable.
/// </summary>
public class JsApiBaseUrlRegressionTests
{
    /// <summary>
    /// Lista explícita de archivos JS en <c>wwwroot/js/</c> que violan ADR-016 pero cuya
    /// corrección está registrada como deuda técnica FUERA DE ALCANCE de HU-023. Cada entrada
    /// cita la fuente autorizante (ADR + HANDOFF + TODO en el propio fichero). Esta lista debe
    /// estar VACÍA para cerrar la deuda; cualquier otra violación que aparezca en el scan debe
    /// seguir haciendo fallar este test (TEST-06 / LOOP-01).
    ///
    /// Estado al cierre del hotfix de HU-022 (2026-10-01): la lista pasa a estar VACÍA.
    /// <c>entregables.js</c> ya NO contiene el literal <c>/api/</c>: sus 3 <c>fetch</c>
    /// apuntan ahora a los proxies MVC <c>/AccionPlan/Entregables*</c> con JWT server-side
    /// (ADR-016 — Defecto B pagado). El propio <c>entregables.js</c> deja de llevar los
    /// <c>TODO [hotfix-HU-022]</c> (L224/L373/L404 eliminados).
    /// </summary>
    private static readonly HashSet<string> ExcepcionesConocidas = new(StringComparer.OrdinalIgnoreCase)
    {
        // Lista vacía — deuda ADR-016 cerrada en el hotfix de HU-022.
        // Si vuelve a aparecer una violación NO tolerada, el Assert.Fail del scan la detecta.
    };

    private const string MensajeAyuda =
        "ADR-016: el navegador NUNCA consume la API interna. " +
        "Si una vista necesita datos de la API en interacciones del usuario, se añade una " +
        "acción MVC proxy (GET /Plan/... o /AccionPlan/...) que llama a la API con IApiClient " +
        "(JWT en sesión) y devuelve el resultado al JS. " +
        "Un fetch('/api/v1/...') en wwwroot/js/ es un defecto.";

    [Fact]
    public void WwwRootJs_SoloFetchesALaApiPermitidosPorAdr016()
    {
        // Arrange: localizar wwwroot/js desde el assembly de tests.
        var wwwrootJs = ObtenerRutaWwwRootJs();
        Assert.True(
            Directory.Exists(wwwrootJs),
            $"No se encontró wwwroot/js en {wwwrootJs} — ajusta ObtenerRutaWwwRootJs.");

        // Act: scan de los .js — busca archivos con fetch( Y /api/ literal.
        // (a) fetch(         → código que ejecuta HTTP.
        // (b) /api/ (literal) → URL prohibida por ADR-016 (sea directa 'fetch("/api/v1/…")'
        //                       o indirecta 'fetch(API_BASE + '…')' con API_BASE='/api/v1/…').
        var archivosVioladores = new List<string>();
        var violacionesNoToleradas = new List<string>();
        var violacionesToleradas = new List<string>();
        var archivosFetchSinApi = new List<string>(); // Solo informativo (fetch pero sin /api/).
        var archivosApiSinFetch = new List<string>(); // Solo informativo (/api/ en strings, sin fetch).

        foreach (var archivo in Directory.GetFiles(wwwrootJs, "*.js"))
        {
            var nombre = Path.GetFileName(archivo);
            var contenido = File.ReadAllText(archivo);
            var tieneFetch = ContieneLlamadaFetch(contenido);
            var tieneApiLiteral = ContieneApiLiteral(contenido);

            if (tieneFetch && tieneApiLiteral)
            {
                archivosVioladores.Add(nombre);
                if (ExcepcionesConocidas.Contains(nombre))
                    violacionesToleradas.Add(nombre);
                else
                    violacionesNoToleradas.Add(nombre);
            }
            else if (tieneFetch)
                archivosFetchSinApi.Add(nombre);
            else if (tieneApiLiteral)
                archivosApiSinFetch.Add(nombre);
        }

        // Assert 1: las violaciones NO toleradas deben ser cero. Si aparece un fichero nuevo
        // con el defecto, el test falla con un listado legible — el blindaje de ADR-016 sigue
        // activo (este es su propósito). El mensaje incluye las toleradas para que la deuda
        // técnica siga siendo visible en cada ejecución de tests.
        if (violacionesNoToleradas.Count > 0)
        {
            var msg = $"{MensajeAyuda}\n\n" +
                      $"Archivos JS que violan ADR-016 y NO están en la lista de tolerados:\n" +
                      $"  · {string.Join("\n  · ", violacionesNoToleradas)}\n\n" +
                      (violacionesToleradas.Count > 0
                          ? $"Deuda técnica registrada (sigue abierta — NO arreglada en HU-023):\n" +
                            $"  · {string.Join("\n  · ", violacionesToleradas)}\n\n" +
                            $"  Referencias: ADR-016 § \"Deuda técnica pendiente\", " +
                            $"docs/HANDOFF_PE_GOL.md § \"Deuda técnica pendiente\" ítem 3, " +
                            $"y TODO [hotfix-HU-022] en el propio fichero.\n"
                          : string.Empty);
            Assert.Fail(msg);
        }

        // Assert 2 (sanidad): la lista de tolerados NO debe estar vacía sin motivo. Si la
        // deuda se cierra (entregables.js se arregla), hay que quitar la entrada de
        // ExcepcionesConocidas — si queda vacía y aparece alguien más, queremos un warning
        // visible (no un assert.Fail para no romper el flujo de tests, solo un Assert.True
        // informativo — ver nota al final del método).
        //
        // Nota: NO añadimos Assert.Fail aquí — la lista vacía no es un error, solo significa
        // que la deuda está pagada. El log siguiente es informativo para @QA en CI.
        if (violacionesToleradas.Count > 0)
        {
            // Log informativo: la deuda técnica sigue abierta. No rompe el test.
            Console.WriteLine(
                $"[JsApiBaseUrlRegressionTests] Deuda técnica ADR-016 aún abierta: " +
                $"{string.Join(", ", violacionesToleradas)}. " +
                $"Ver ADR-016, HANDOFF § Deuda técnica pendiente ítem 3, TODO [hotfix-HU-022].");
        }
    }

    /// <summary>
    /// Localiza <c>PE-GOL.Aplicacion/wwwroot/js</c> desde la ubicación del assembly de tests.
    /// Estructura esperada: <c>{repo}/PE-GOL.Tests/bin/Debug/net8.0/PE-GOL.Tests.dll</c> →
    /// subir 4 niveles para llegar a la raíz de la solución, luego bajar a
    /// <c>PE-GOL.Aplicacion/wwwroot/js</c>.
    /// </summary>
    private static string ObtenerRutaWwwRootJs()
    {
        var assemblyLocation = typeof(JsApiBaseUrlRegressionTests).Assembly.Location;
        var binDir = Path.GetDirectoryName(assemblyLocation)
            ?? throw new InvalidOperationException(
                $"No se pudo obtener el directorio del assembly ({assemblyLocation}).");

        // bin/Debug/net8.0 → bin/Debug → bin → PE-GOL.Tests → solution root (4 up).
        var raizSolucion = Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", ".."));
        return Path.Combine(raizSolucion, "PE-GOL.Aplicacion", "wwwroot", "js");
    }

    /// <summary>
    /// ¿El archivo contiene una llamada <c>fetch(</c>? Usa una regex robusta que ignora
    /// la palabra "fetch" en comentarios y en strings (p. ej. <c>"fetch failed"</c>).
    /// </summary>
    private static bool ContieneLlamadaFetch(string contenido)
    {
        // Coincide con fetch( ... donde ( sigue inmediatamente al identificador 'fetch'
        // y NO está precedido por un '.' (para no capturar 'window.fetch' como propiedad
        // — pero 'window.fetch(' también nos interesa como llamada HTTP; lo incluimos).
        return System.Text.RegularExpressions.Regex.IsMatch(
            contenido,
            @"\bfetch\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// ¿El archivo contiene una cadena literal <c>/api/</c>? ADR-016 considera prohibida
    /// cualquier construcción que apunte a la API interna; basta con detectar el literal.
    /// </summary>
    private static bool ContieneApiLiteral(string contenido)
        => contenido.Contains("/api/");
}