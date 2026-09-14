using PE_GOL.Utility.Constants;
using PE_GOL.Utility.Helpers;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para ZonaHorariaHelper — Spec HU-006 § "Tests requeridos" (4 casos, tabla #19-22).
/// TDD fase red (TEST-01): el stub ZonaHorariaHelper.EsValida lanza NotImplementedException, por lo
/// que los 4 tests fallan deliberadamente EN RUNTIME hasta que @BackendDev implemente la lógica en
/// fase 4 (spec § Lógica BLL paso 4, D7: lista curada ZonasIANA + fallback TimeZoneInfo).
/// La lista ZonasIANA (Constants) es un stub MÍNIMO real (contiene los IDs que ejercitan los tests);
/// @BackendDev completará la lista curada ≈ 140 identificadores IANA en fase 4.
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// </summary>
public class ZonaHorariaHelperTests
{
    // Caso 19 ─ IDs IANA estándar → true (D7: lista curada + fallback TimeZoneInfo)
    [Fact]
    public void EsValida_ZonaIANACorrecta_RetornaTrue()
    {
        // Arrange & Act & Assert: la lista curada contiene el identificador por defecto del
        // dominio (DDL tenant.zona_horaria) y los IDs que ejercitan el fallback TimeZoneInfo
        Assert.Contains("America/Managua", ZonasIANA.Zonas);
        Assert.Contains("America/Mexico_City", ZonasIANA.Zonas);
        Assert.Contains("UTC", ZonasIANA.Zonas);

        // IDs IANA estándar → true
        Assert.True(ZonaHorariaHelper.EsValida("America/Managua"));
        Assert.True(ZonaHorariaHelper.EsValida("America/Mexico_City"));
        Assert.True(ZonaHorariaHelper.EsValida("UTC"));
    }

    // Caso 20 ─ zona inexistente → false
    [Fact]
    public void EsValida_ZonaInexistente_RetornaFalse()
    {
        // Arrange & Act & Assert: "Foo/Bar" no es un identificador IANA → false
        Assert.False(ZonaHorariaHelper.EsValida("Foo/Bar"));
    }

    // Caso 21 ─ null/vacío → false (paso 1 de la lógica)
    [Fact]
    public void EsValida_NullOVacio_RetornaFalse()
    {
        // Arrange & Act & Assert: null, string vacío y solo espacios → false
        Assert.False(ZonaHorariaHelper.EsValida(null));
        Assert.False(ZonaHorariaHelper.EsValida(string.Empty));
        Assert.False(ZonaHorariaHelper.EsValida("   "));
    }

    // Caso 22 ─ los IDs IANA son case-sensitive (D7) → minúsculas = false
    [Fact]
    public void EsValida_CaseSensitive_RetornaFalse()
    {
        // Arrange & Act & Assert: "america/managua" (minúsculas) NO es válido
        Assert.False(ZonaHorariaHelper.EsValida("america/managua"));
    }
}