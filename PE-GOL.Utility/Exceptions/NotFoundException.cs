namespace PE_GOL.Utility.Exceptions;

/// <summary>
/// Recurso inexistente → HTTP 404 (Spec HU-001 § Lógica BLL).
/// Reubicada desde PE-GOL.BLL/Exceptions por @BackendDev (Paso 1.5): ahora vive en
/// PE-GOL.Utility/Exceptions como excepción transversal (04_ARQUITECTURA.md § 2).
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}