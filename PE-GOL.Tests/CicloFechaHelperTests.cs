using System;
using PE_GOL.Utility.Helpers;
using Xunit;

namespace PE_GOL.Tests;

public class CicloFechaHelperTests
{
    [Fact]
    public void ObtenerRango_Enero_CalculaRangoCalendarioCorrectamente()
    {
        // Arrange
        int año = 2026;
        int mes = 1;

        // Act
        var rango = CicloFechaHelper.ObtenerRango(año, mes);

        // Assert
        Assert.Equal(new DateOnly(2026, 1, 1), rango.Inicio);
        Assert.Equal(new DateOnly(2026, 12, 31), rango.Fin);
    }

    [Fact]
    public void ObtenerRango_MesDistintoEnero_CalculaRangoCruzadoCorrectamente()
    {
        // Arrange
        int año = 2026;
        int mes = 6;

        // Act
        var rango = CicloFechaHelper.ObtenerRango(año, mes);

        // Assert
        Assert.Equal(new DateOnly(2026, 6, 1), rango.Inicio);
        Assert.Equal(new DateOnly(2027, 5, 31), rango.Fin);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void ObtenerRango_MesInvalido_LanzaException(int mesInvalido)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => CicloFechaHelper.ObtenerRango(2026, mesInvalido));
    }
}