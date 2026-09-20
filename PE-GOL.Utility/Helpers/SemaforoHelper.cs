namespace PE_GOL.Utility.Helpers;

/// <summary>
/// Evaluador de semáforos del ciclo (Spec HU-015 § Lógica BLL, D-I; 04_ARQUITECTURA.md L173).
/// Regla (05_DOMINIO.md L158-161): valor &gt;= umbralVerde → "Verde"; valor &gt;= umbralAmarillo →
/// "Amarillo"; else → "Rojo". Comparaciones INCLUSIVAS en ambos bordes (casos 22/24 de @QA).
/// Código propio, sin librerías externas: reutilizable por HU-016/017/024/038/039.
/// </summary>
public static class SemaforoHelper
{
    /// <summary>Evalúa un valor 0..1 contra los umbrales del ciclo (05_DOMINIO.md L158-161).</summary>
    public static string Evaluar(decimal valor, decimal umbralVerde, decimal umbralAmarillo)
    {
        if (valor >= umbralVerde) return "Verde";
        if (valor >= umbralAmarillo) return "Amarillo";
        return "Rojo";
    }
}