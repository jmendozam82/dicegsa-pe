namespace PE_GOL.DTO.Limites;

/// <summary>
/// Límites configurados de un plan (mapeo de plan.max_areas/max_usuarios/max_ciclos_activos).
/// Spec HU-002 § DTOs — contrato del validador de límites (cruza BLL ↔ DAL; DAL no puede
/// referenciar BLL [ARCH-02], por eso vive en PE_GOL.DTO.Limites).
/// DECISIÓN de @QA (2026-09-13): el spec aprobado define `Desde(PlanEntity plan)`, pero @QA
/// lo entrega como `Desde(int, int, int)` para NO acoplar PE-GOL.DTO → PE-GOL.Entity (DTO
/// permanece sin dependencias; el servicio hace el mapeo plan → límites). Ver reporte @QA HU-002.
/// </summary>
public sealed record PlanLimits(int MaxAreas, int MaxUsuarios, int MaxCiclosActivos)
{
    /// <summary>Crea límites desde valores atómicos (el servicio los obtiene de PlanEntity).</summary>
    public static PlanLimits Desde(int maxAreas, int maxUsuarios, int maxCiclosActivos) =>
        new(maxAreas, maxUsuarios, maxCiclosActivos);
}