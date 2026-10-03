using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.DTO.Dtos;

/// <summary>
/// Proyección DAL de okr (Spec HU-026 · addendum exportación). Derivada de
/// <see cref="OkrResponse"/> para que los dobles de test puedan expresar
/// <c>ReturnsAsync((OkrDto?)null)</c> contra el contrato <c>Task{OkrResponse?}</c>.
/// </summary>
public class OkrDto : OkrResponse
{
}
