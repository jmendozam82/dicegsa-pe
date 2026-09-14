using PE_GOL.Utility.Constants;

namespace PE_GOL.Utility.Helpers;

/// <summary>
/// Helper de zonas horarias IANA (Spec HU-006, D7) — lógica pura, determinista para tests.
/// EsValida: (1) null/vacío → false; (2) ZonasIANA.Contains(zona) — lista curada case-sensitive;
/// (3) fallback TimeZoneInfo.TryFindSystemTimeZoneById (acepta IDs IANA con ICU en .NET 8);
/// retorna el OR de (2) y (3). Evita el paquete NuGet TimeZoneConverter (fuera del stack).
/// Implementación real (fase 4 del Loop); los tests de @QA son la especificación ejecutable.
/// </summary>
public static class ZonaHorariaHelper
{
    /// <summary>
    /// Valida que la zona sea un identificador IANA válido (case-sensitive, D7).
    /// null/vacío/solo espacios → false. Lista curada ZonasIANA + fallback TimeZoneInfo.
    /// </summary>
    public static bool EsValida(string? zona)
    {
        // Paso 1: null/vacío → false.
        if (string.IsNullOrWhiteSpace(zona))
            return false;

        // Paso 2: lista curada case-sensitive (StringComparer.Ordinal).
        if (ZonasIANA.Zonas.Contains(zona))
            return true;

        // Paso 3: fallback TimeZoneInfo (IDs IANA con ICU en .NET 8). TryFindSystemTimeZoneById
        // no lanza (patrón Try) y es case-sensitive en la conversión IANA→Windows (Windows) y en
        // el lookup de tzdata (Linux) → "america/managua" en minúsculas NO resuelve (test #22).
        return TimeZoneInfo.TryFindSystemTimeZoneById(zona, out _);
    }
}