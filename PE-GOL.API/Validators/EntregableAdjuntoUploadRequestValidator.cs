using FluentValidation;
using PE_GOL.DTO.Requests.Objetivos;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Helpers;

namespace PE_GOL.API.Validators;

/// <summary>
/// Capa 1 de validaciones de HU-022 — FORMA (STACK-04, UX-04). Valida el
/// <see cref="EntregableAdjuntoUploadRequest"/> que el controller proyecta desde el multipart.
/// <para>
/// <b>Solo forma.</b> Los umbrales de negocio (conteo total frente a los adjuntos YA existentes,
/// firma real de bytes, estado del ciclo) los aplica
/// <c>PE-GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs</c> con código puro, y el 422 se
/// resuelve también vía la BLL. La validación del servidor es siempre la fuente de verdad (UX-04).
/// </para>
/// <para>
/// <b>Por qué aquí y no en la BLL:</b> los 32 <c>AbstractValidator</c> del repositorio están en
/// <c>PE-GOL.API/Validators/</c>, y <c>PE-GOL.BLL.csproj</c> no referencia FluentValidation —
/// moverlo allí tocaría el grafo de proyectos sin necesidad (corrección 5 del spec v3).
/// </para>
/// <para>
/// Los umbrales numéricos salen de <see cref="EntregableAdjuntoReglas"/> y la allowlist de
/// <see cref="TipoArchivoHelper"/>: cambiar RN-020 o el CA #2 se hace en un único sitio (D-L).
/// </para>
/// </summary>
public class EntregableAdjuntoUploadRequestValidator : AbstractValidator<EntregableAdjuntoUploadRequest>
{
    public EntregableAdjuntoUploadRequestValidator()
    {
        RuleFor(x => x.AccionId)
            .NotEmpty().WithMessage("El identificador de la acción es obligatorio.");

        RuleFor(x => x.Archivos)
            .NotNull()
            .Must(a => a is not null
                       && a.Count >= 1
                       && a.Count <= EntregableAdjuntoReglas.MaximoArchivosPorAccion)
            .WithMessage($"Debe enviar entre 1 y {EntregableAdjuntoReglas.MaximoArchivosPorAccion} archivos.");

        RuleForEach(x => x.Archivos).SetValidator(new ArchivoSubidaMetadataValidator());
    }
}

/// <summary>
/// Metadatos de UN archivo: nombre, tamaño y tipo. Es la regla que se aplica a cada elemento de
/// <see cref="EntregableAdjuntoUploadRequest.Archivos"/>.
/// </summary>
public class ArchivoSubidaMetadataValidator : AbstractValidator<ArchivoSubidaMetadata>
{
    public ArchivoSubidaMetadataValidator()
    {
        RuleFor(x => x.NombreArchivo)
            .NotEmpty().WithMessage("El nombre del archivo es obligatorio.")
            .MaximumLength(EntregableAdjuntoReglas.LongitudMaximaNombre)
                .WithMessage($"El nombre no puede superar {EntregableAdjuntoReglas.LongitudMaximaNombre} caracteres.")
            .Must(n => !string.IsNullOrEmpty(n) && Path.GetFileName(n) == n)
                .WithMessage("El nombre del archivo no puede contener ruta.");

        RuleFor(x => x.TamanoBytes)
            .GreaterThan(0).WithMessage("El archivo está vacío.")
            .LessThanOrEqualTo(EntregableAdjuntoReglas.TamanoMaximoBytes)
                .WithMessage($"El archivo no puede superar {EntregableAdjuntoReglas.TamanoMaximoBytes / (1024 * 1024)} MB por archivo.");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("El tipo MIME del archivo es obligatorio.");

        RuleFor(x => x.ExtensionDeNombre)
            .NotEmpty().WithMessage("La extensión del archivo es obligatoria.")
            .Must(e => !string.IsNullOrEmpty(e)
                       && TipoArchivoHelper.ExtensionesPermitidas.Contains(e, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Tipo de archivo no permitido. Permitidos: PDF, DOCX, XLSX, PNG, JPG.");
    }
}
