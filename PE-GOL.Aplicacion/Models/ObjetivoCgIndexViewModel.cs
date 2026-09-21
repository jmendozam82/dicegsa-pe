using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del listado de Objetivos CG del Jefe de Área (Spec HU-017 § UI — Index.cshtml).
/// Datos: GET /api/v1/objetivos-cg (solo JefeArea; SEC-07: la API filtra por AreaId del JWT — la UI
/// nunca envía area_id, SEC-06). El ciclo activo se resuelve server-side (D-G).
/// </summary>
public class ObjetivoCgIndexViewModel
{
    public List<ObjetivoCgResponse> Items { get; set; } = [];

    /// <summary>Nombre del ciclo activo (contexto del encabezado).</summary>
    public string? CicloNombre { get; set; }

    /// <summary>true si el tenant tiene un ciclo activo (si no, estado vacío).</summary>
    public bool HayCicloActivo { get; set; }
}