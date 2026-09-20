using PE_GOL.Utility.Helpers;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para SemaforoHelper — Spec HU-015 § "Tests requeridos" (5 casos de la tabla,
/// L194-202). TDD fase red por contrato (TEST-01): SemaforoHelper es un STUB que lanza
/// NotImplementedException (@QA declaró el contrato; @BackendDev implementa en fase 4, D-I) →
/// los 5 tests COMPILAN y FALLAN en runtime (rojo esperado).
/// Regla (05_DOMINIO.md L158-161): valor &gt;= umbralVerde → "Verde"; valor &gt;= umbralAmarillo →
/// "Amarillo"; else → "Rojo". Los casos 22 y 24 cubren los BORDES (igualdad exacta).
/// Patrón Arrange/Act/Assert + nombre [Metodo]_[Escenario]_[ResultadoEsperado] (TEST-03/05).
/// </summary>
public class SemaforoHelperTests
{
    // Caso 21 ─ valor mayor al umbral verde → "Verde"
    [Fact]
    public void Evaluar_ValorMayorIgualVerde_RetornaVerde()
    {
        // Act: 0.95 > 0.90
        var resultado = SemaforoHelper.Evaluar(0.95m, 0.90m, 0.70m);

        // Assert
        Assert.Equal("Verde", resultado);
    }

    // Caso 22 ─ valor IGUAL al umbral verde (borde) → "Verde"
    [Fact]
    public void Evaluar_ValorIgualVerde_RetornaVerde()
    {
        // Act: borde — 0.90 >= 0.90 (comparación inclusiva)
        var resultado = SemaforoHelper.Evaluar(0.90m, 0.90m, 0.70m);

        // Assert
        Assert.Equal("Verde", resultado);
    }

    // Caso 23 ─ valor entre amarillo y verde → "Amarillo"
    [Fact]
    public void Evaluar_ValorEntreAmarilloYVerde_RetornaAmarillo()
    {
        // Act: 0.80 entre 0.70 y 0.90
        var resultado = SemaforoHelper.Evaluar(0.80m, 0.90m, 0.70m);

        // Assert
        Assert.Equal("Amarillo", resultado);
    }

    // Caso 24 ─ valor IGUAL al umbral amarillo (borde) → "Amarillo"
    [Fact]
    public void Evaluar_ValorIgualAmarillo_RetornaAmarillo()
    {
        // Act: borde — 0.70 >= 0.70 (comparación inclusiva)
        var resultado = SemaforoHelper.Evaluar(0.70m, 0.90m, 0.70m);

        // Assert
        Assert.Equal("Amarillo", resultado);
    }

    // Caso 25 ─ valor menor al umbral amarillo → "Rojo"
    [Fact]
    public void Evaluar_ValorMenorAmarillo_RetornaRojo()
    {
        // Act: 0.50 < 0.70
        var resultado = SemaforoHelper.Evaluar(0.50m, 0.90m, 0.70m);

        // Assert
        Assert.Equal("Rojo", resultado);
    }
}