namespace PE_GOL.DTO.Requests;

/// <summary>
/// Request de actualización de ciclo anual (Spec HU-007 § DTOs — PUT /api/v1/ciclos/{id}).
/// Mismos campos y reglas que CicloCreateRequest. Solo se edita un ciclo en estado 'Borrador'
/// (RC-12/RN-004 — validado en BLL). La unicidad de añoFiscal excluye el propio ciclo (DAL-C6).
/// </summary>
public class CicloUpdateRequest
{
    /// <summary>Requerido, máx 100 chars (ej: "PE 2026").</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Requerido, 2000..2100, único en el tenant (excluyendo el propio ciclo).</summary>
    public int AñoFiscal { get; set; }

    /// <summary>Requerido, 1..12 (espejo del CHECK del DDL L134).</summary>
    public int MesInicio { get; set; }
}