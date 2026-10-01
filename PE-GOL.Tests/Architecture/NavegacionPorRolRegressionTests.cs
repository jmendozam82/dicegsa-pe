using System.Text.RegularExpressions;

namespace PE_GOL.Tests.Architecture;

/// <summary>
/// Regresión de navegación por rol en las vistas Razor (HU-022-hotfix v2, defecto J).
/// </summary>
/// <para>
/// El botón «Volver» de <c>Views/AccionPlan/Entregables.cshtml</c> usaba <c>asp-action="Index"</c>
/// sin <c>asp-controller</c>, que el tag helper resuelve contra el controlador EN CURSO:
/// <c>/AccionPlan/Index</c>. Esa acción es <c>[Authorize(Roles = "JefeArea")]</c>, así que el
/// Gerente —que llega a Entregables desde el Plan Consolidado y no tiene otra entrada al
/// listado por CG— pulsaba «Volver» y caía en <c>/Auth/AccessDenied?ReturnUrl=%2FAccionPlan%2FIndex</c>.
/// La validación en navegador (Jorge, 2026-10-01) destapó que 3 de 4 enlaces de la UI apuntaban a
/// una página de otro rol; este test blinda el caso para que no vuelva.
/// </para>
/// <para>
/// Es un test de ARQUITECTURA de plantillas, no de comportamiento: el tag helper se resuelve en
/// tiempo de compilación de la vista, así que un test de controller no lo detectaría. Se apoya en
/// la misma técnica (scan del fichero fuente) que ya usa <see cref="JsApiBaseUrlRegressionTests"/>,
/// aceptada en este repo para blindar invariantes que ningún test de C# puede observar.
/// </para>
/// <para>
/// Coste: O(1) sobre un único fichero de <c>~200</c> líneas. Fragilidad: ata el test al markup de
/// esa vista; si se cambia la redacción del enlace hay que actualizar las aserciones, que es
/// exactamente lo que se quiere que sea visible en el diff cuando cambie la navegación.
/// </para>
/// </summary>
public class NavegacionPorRolRegressionTests
{
    private static string ObtenerVistaEntregables()
    {
        var assemblyLocation = typeof(NavegacionPorRolRegressionTests).Assembly.Location;
        var binDir = Path.GetDirectoryName(assemblyLocation)
            ?? throw new InvalidOperationException(
                $"No se pudo obtener el directorio del assembly ({assemblyLocation}).");

        // bin/Debug/net8.0 → bin/Debug → bin → PE-GOL.Tests → solution root (4 up).
        var raizSolucion = Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", ".."));
        var vista = Path.Combine(raizSolucion, "PE-GOL.Aplicacion", "Views", "AccionPlan", "Entregables.cshtml");
        Assert.True(File.Exists(vista), $"No se encontró la vista {vista}.");
        return File.ReadAllText(vista);
    }

    /// <summary>
    /// Regresión J: el enlace de «Volver» no puede resolver contra el <c>Index</c> implícito del
    /// controlador en curso, porque esa acción es exclusiva de JefeArea. Cualquier etiqueta
    /// <c>&lt;a&gt;</c> que apunte a <c>Index</c> tiene que declarar su <c>asp-controller</c>.
    /// </summary>
    [Fact]
    public void Entregables_EnlaceVolver_NoResuelveAlIndexJefeAreaDeFormaImplicit()
    {
        // Arrange
        var vista = ObtenerVistaEntregables();

        // Act: se recorren TODAS las etiquetas <a>, en cualquier orden de atributos y con los
        // atributos repartidos en varias líneas (que es como se escriben en estas vistas).
        var anclas = Regex.Matches(vista, "<a\\b[^>]*>", RegexOptions.Singleline);
        var anclasIndiceImplicit = anclas
            .Select(m => m.Value)
            .Where(tag => tag.Contains("asp-action=\"Index\"", StringComparison.Ordinal))
            .Where(tag => !tag.Contains("asp-controller=", StringComparison.Ordinal))
            .ToList();

        // Assert
        Assert.True(
            anclas.Count > 0,
            "No se encontró ninguna etiqueta <a> en Entregables.cshtml: el scan estaría vacío y "
            + "pasaría sin comprobar nada.");
        Assert.True(
            anclasIndiceImplicit.Count == 0,
            "El enlace «Volver» de Entregables.cshtml apunta a asp-action=\"Index\" sin declarar "
            + $"asp-controller, que resuelve contra /AccionPlan/Index ([Authorize(Roles = \"JefeArea\")]) "
            + $"y manda al Gerente a AccessDenied. Etiquetas offendidas: {string.Join(" | ", anclasIndiceImplicit)} "
            + "(defecto J, HU-022-hotfix v2).");
    }

    /// <summary>
    /// El Gerente vuelve al Plan Consolidado, que es de donde llega, y el Jefe de Área a su
    /// listado por CG. Ambos destinos tienen que estar presentes en la vista.
    /// </summary>
    [Fact]
    public void Entregables_EnlaceVolver_DistingueGerenteDeJefeDeArea()
    {
        // Arrange
        var vista = ObtenerVistaEntregables();

        // Act
        var tieneDestinoGerente = vista.Contains("Plan\"", StringComparison.Ordinal)
                                  && vista.Contains("Consolidado", StringComparison.Ordinal);
        var tieneDestinoJefeArea = Regex.IsMatch(
            vista,
            "asp-controller=\"AccionPlan\"\\s*asp-action=\"Index\"|asp-action=\"Index\"\\s*asp-controller=\"AccionPlan\"",
            RegexOptions.Singleline);
        var tieneGuardaDeRol = vista.Contains("""IsInRole("Gerente")""", StringComparison.Ordinal);

        // Assert
        Assert.True(tieneGuardaDeRol, "El enlace «Volver» debe estar guardado por rol (User.IsInRole).");
        Assert.True(
            tieneDestinoGerente,
            "El Gerente debe volver a /Plan/Consolidado, de donde llega a Entregables.");
        Assert.True(
            tieneDestinoJefeArea,
            "El Jefe de Área debe volver a /AccionPlan/Index, su listado por CG.");
    }
}
