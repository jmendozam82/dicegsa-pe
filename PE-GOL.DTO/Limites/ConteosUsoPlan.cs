namespace PE_GOL.DTO.Limites;

/// <summary>
/// Uso actual de un tenant (retornado por el DAL; SIN dependencia de BLL).
/// Spec HU-002 § DTOs — contrato del validador de límites.
/// Semántica (spec § Lógica BLL paso 8 / D7-D5): AreasActuales = máximo de áreas activas en
/// un solo ciclo; UsuariosActuales = TODOS los usuarios del tenant (incl. Inactivo/Bloqueado);
/// CiclosActivosActuales = ciclos con estado 'Activo'.
/// </summary>
public sealed record ConteosUsoPlan(int AreasActuales, int UsuariosActuales, int CiclosActivosActuales);