using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de responsable (Spec HU-010 § UI — Crear/Editar.cshtml).
/// Crear: nombre + correo + área (POST /api/v1/ciclos/{cicloId}/responsables).
/// Editar: SOLO reasignación de área (PUT .../responsables/{id} con ResponsableReassignRequest);
/// nombre/correo son de solo lectura (el contrato HU-010 no los edita).
/// Areas: catálogo GET /api/v1/ciclos/{cicloId}/areas para el select de área destino.
/// </summary>
public class ResponsableFormViewModel
{
    public Guid CicloId { get; set; }
    public Guid ResponsableId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public Guid AreaId { get; set; }

    /// <summary>true en Editar: nombre/correo de solo lectura + badge de estado.</summary>
    public bool EsEdicion { get; set; }

    /// <summary>Activo | Inactivo | Bloqueado — solo lectura (badge).</summary>
    public string Estado { get; set; } = string.Empty;

    public List<AreaResponse> Areas { get; set; } = [];
}