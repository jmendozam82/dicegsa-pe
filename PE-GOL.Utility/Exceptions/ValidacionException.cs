namespace PE_GOL.Utility.Exceptions;

/// <summary>
/// Regla de negocio violada → HTTP 422 (Spec HU-001 § Lógica BLL).
/// Reubicada desde PE-GOL.BLL/Exceptions por @BackendDev (Paso 1.5): ahora vive en
/// PE-GOL.Utility/Exceptions como excepción transversal (04_ARQUITECTURA.md § 2).
/// </summary>
public class ValidacionException : Exception
{
    public ValidacionException(string message) : base(message) { }
}