using System;

namespace PE_GOL.Utility.Helpers;

public static class CicloFechaHelper
{
    public static (DateOnly Inicio, DateOnly Fin) ObtenerRango(int añoFiscal, int mesInicio)
    {
        if (mesInicio < 1 || mesInicio > 12)
            throw new ArgumentOutOfRangeException(nameof(mesInicio), "El mes de inicio debe estar entre 1 y 12.");

        var inicio = new DateOnly(añoFiscal, mesInicio, 1);
        var fin = inicio.AddYears(1).AddDays(-1);

        return (inicio, fin);
    }
}