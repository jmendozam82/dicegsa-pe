using PE_GOL.DTO.Limites;

namespace PE_GOL.BLL.Limites;

/// <summary>
/// Validador PURO de límites de plan (sin dependencias de BD ni I/O) — Spec HU-002 § Contratos.
/// 100% determinista → ideal para TDD unitario (@QA escribe sus tests contra esta clase).
/// Lógica implementada conforme al spec § Lógica BLL paso 8 (fase 4):
///   · comparación estricta `>` (conteos == límite ⇒ válido);
///   · mensajes exactos del spec:
///     - "El tenant supera el límite de áreas: {AreasActuales} > {MaxAreas}"
///     - "El tenant supera el límite de usuarios: {UsuariosActuales} > {MaxUsuarios}"
///     - "El tenant supera el límite de ciclos activos: {CiclosActivosActuales} > {MaxCiclosActivos}"
///   · acumula TODOS los recursos infractores (no solo el primero).
/// Semántica de los conteos (D5/D7): AreasActuales = máximo de áreas activas en un solo ciclo;
/// UsuariosActuales = todos los usuarios del tenant (incl. Inactivo/Bloqueado);
/// CiclosActivosActuales = ciclos con estado 'Activo'.
/// </summary>
public sealed class PlanLimitValidator
{
    /// <summary>Prefijo de los mensajes del validador (constante compartida para la
    /// agregación con nombre de tenant en PlanService.ValidarLimitesParaTenantsDelPlanAsync).</summary>
    public const string PrefijoMensaje = "El tenant";

    /// <summary>Compara conteos de uso vs límites. Devuelve Resultado con mensajes de error
    /// por cada recurso excedido (áreas / usuarios / ciclos activos).</summary>
    public ResultadoValidacionLimites Validar(PlanLimits limites, ConteosUsoPlan conteos)
    {
        var errores = new List<string>();

        if (conteos.AreasActuales > limites.MaxAreas)
            errores.Add($"El tenant supera el límite de áreas: {conteos.AreasActuales} > {limites.MaxAreas}");

        if (conteos.UsuariosActuales > limites.MaxUsuarios)
            errores.Add($"El tenant supera el límite de usuarios: {conteos.UsuariosActuales} > {limites.MaxUsuarios}");

        if (conteos.CiclosActivosActuales > limites.MaxCiclosActivos)
            errores.Add($"El tenant supera el límite de ciclos activos: {conteos.CiclosActivosActuales} > {limites.MaxCiclosActivos}");

        return errores.Count == 0
            ? ResultadoValidacionLimites.Ok()
            : ResultadoValidacionLimites.Fallo(errores);
    }

    /// <summary>Atajo: true si ningún límite se excede.</summary>
    public bool EsValido(PlanLimits limites, ConteosUsoPlan conteos)
        => Validar(limites, conteos).EsValido;
}