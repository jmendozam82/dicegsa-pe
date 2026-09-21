using PE_GOL.DTO.Responses;

namespace PE_GOL.Aplicacion.Models;

/// <summary>
/// ViewModel del formulario de Objetivo CG (Spec HU-017 § UI — Crear/Editar.cshtml).
/// Codigo "GOLn.CGm" es auto-generado por la BLL (CA #1, D-B) — nunca viaja en el request;
/// en Editar se muestra como badge de solo lectura. CA #3: el select de pilar expone
/// ObjetivoQ1..Q4 (HU-014) vía data-* para el panel de referencia contextual (JS, sin AJAX).
/// </summary>
public class ObjetivoCgFormViewModel
{
    public Guid ObjetivoCgId { get; set; }

    /// <summary>"GOLn.CGm" auto-generado (CA #1) — solo lectura en Editar.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;
    public Guid PilarId { get; set; }

    /// <summary>"Q1" | "Q2" | "Q3" | "Q4".</summary>
    public string TrimestreObjetivo { get; set; } = string.Empty;

    /// <summary>Catálogo de pilares del ciclo activo (CA #3 — referencia contextual).</summary>
    public List<PilarResponse> Pilares { get; set; } = [];

    /// <summary>true en Editar: el CG existe (para mostrar código GOLn.CGm).</summary>
    public bool EsEdicion { get; set; }
}