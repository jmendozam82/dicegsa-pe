namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de clonación de ciclo anual (Spec HU-007 § DTOs — POST /api/v1/ciclos/{id}/clonar).
/// Define la identidad del ciclo NUEVO; el origen es el {id} de la ruta. La clonación copia los
/// umbrales de semáforo del origen (D-B, CA #5 parcial); áreas/responsables se difieren a
/// HU-009/HU-010 (Flag #2).
/// </summary>
public class ClonarCicloRequest
{
    /// <summary>Requerido, máx 100 chars (ej: "PE 2027").</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Requerido, 2000..2100, único en el tenant.</summary>
    public int AñoFiscal { get; set; }

    /// <summary>Requerido, 1..12 (espejo del CHECK del DDL L134).</summary>
    public int MesInicio { get; set; }
}