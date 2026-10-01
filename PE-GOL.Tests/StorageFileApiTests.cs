using System.Text;
using Moq;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Storage;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-022 — casos <b>58-61</b> (spec <b>v3</b> § "Tests requeridos"), los
/// 4 de Storage. Escritos ANTES de la implementación (TEST-01): <b>el rojo legítimo es el fallo de
/// compilación</b> (no existen <c>IStorageFileApi</c>, <c>FileUploadOptions</c>,
/// <c>StorageUploadResult</c> ni los 3 métodos genéricos de <c>IStorageHelper</c>). Misma convención
/// que AccionPlanGanttServiceTests (HU-021).
/// <para>
/// <b>Por qué <c>Mock&lt;IStorageFileApi&gt;</c> y NO <c>ISupabaseClient</c></c> (corrección 6 / D-O
/// del spec, y F1 resuelto por Jorge el 2026-09-27): <c>StorageHelper</c> construía
/// <c>Supabase.Client</c> en su propio constructor, de modo que era IMPOSIBLE testearlo sin red.
/// La extracción de <c>IStorageFileApi</c> lo resuelve: <c>Supabase.Client</c> queda confinado a
/// <c>SupabaseStorageFileApi</c> y estos 4 tests no abren ninguna conexión.
/// </para>
/// <para>
/// <b>CONTRATO QUE @BackendDev DEBE IMPLEMENTAR — EXACTO, sin adivinar (bloqueo 3 de la v3).</b>
/// El spec v2 NOMBRABA los 3 tipos pero no los declaraba, así que estos tests no tenían contrato
/// contra el que escribir. La v3 los declara con firma exacta:
/// <code>
/// namespace PE_GOL.Utility.Storage;
///
/// public interface IStorageFileApi
/// {
///     Task&lt;StorageUploadResult?&gt; UploadAsync(string bucket, string path, Stream content,
///                                             FileUploadOptions? options, CancellationToken ct = default);
///     Task&lt;string?&gt;            CreateSignedUrlAsync(string bucket, string path, TimeSpan expiresIn, CancellationToken ct = default);
///     Task&lt;bool&gt;               RemoveAsync(string bucket, string path, CancellationToken ct = default);
/// }
///
/// public sealed record FileUploadOptions(string? ContentType = null,
///                                        string? ContentDisposition = null,
///                                        bool Upsert = false);
/// public sealed record StorageUploadResult(string Path, long SizeBytes, string? ETag);
/// </code>
/// Ojo al ORDEN de los parámetros de <c>UploadAsync</c>: <b>bucket, path, content</b> (el spec v1
/// decía bucket, stream, path) y el resultado es <c>StorageUploadResult?</c>, no <c>Task</c>: es la
/// señal de fallo que la BLL usa para compensar (D-H, RNF-014).
/// <para>
/// Y <c>IStorageHelper</c> crece con <b>3 métodos genéricos</b> (los 2 de HU-006 intactos), con la
/// ENMIENDA 2 de la v3 — el 4.º parámetro es el <b>TipoArchivo</b> canónico, no un string de
/// content-disposition (bloqueo 8):
/// <code>
/// Task&lt;string?&gt; SubirArchivoAsync(string bucket, string path, byte[] bytes, TipoArchivo tipo, CancellationToken ct = default);
/// Task&lt;string?&gt; ObtenerUrlFirmadaAsync(string bucket, string path, int horas, CancellationToken ct = default);   // SOBRECARGA, SIN contentDisposition
/// Task&lt;bool&gt;    EliminarArchivoAsync(string bucket, string path, CancellationToken ct = default);
/// </code>
/// La sobrecarga de <c>ObtenerUrlFirmadaAsync</c> convive con la de HU-006
/// (<c>(string path, TimeSpan expiracion, ct)</c>): aridades distintas, sin ambigüedad.
/// <c>StorageHelper</c> pasa a <c>ctor(SupabaseStorageOptions options, IStorageFileApi fileApi)</c>
/// y <c>SupabaseStorageOptions</c> gana <c>EntregablesBucket</c> con default <c>"entregables"</c>.
/// </para>
/// <para>
/// <b>BLOQUEO 8 · El <c>content-disposition</c> viaja SOLO en la SUBIDA</b>, dentro de
/// <c>FileUploadOptions.ContentDisposition = TipoArchivoHelper.ContentDisposition(tipo)</c>. El caso 58
/// lo afirma; y <c>ObtenerUrlFirmadaAsync(bucket, path, horas, ct)</c> <b>NO</b> lleva ese parámetro
/// (ni el spec v3 ni ADR-013 lo amplían): una URL firmada no puede reescribir el
/// <c>content-disposition</c> de un objeto ya subido, y el valor ya quedó persistido en el Upload.
/// Si <c>@Arquitecto</c> llegara a ampliar la firma, el caso 58 es el que documenta el contrato
/// vigente y la enmienda 3 de ADR-013 es la que lo fija.
/// </para>
/// <para>
/// Estos 4 casos cubren <c>PE-GOL.Utility</c> y <b>NO computan para TEST-02</b>, que mide
/// <c>PE-GOL.BLL</c> (spec v3 § "Cobertura").
/// </para>
/// </summary>
public class StorageFileApiTests
{
    private static readonly Guid TenantId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CicloId   = Guid.Parse("00000000-0000-0000-0000-000000000030");
    private static readonly Guid AccionId  = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private const string Bucket = "entregables";

    private readonly Mock<IStorageFileApi> _api = new();
    private Stream? _flujoRecibido;
    private byte[]? _bytesRecibidos;
    private FileUploadOptions? _opcionesRecibidas;

    /// <summary>
    /// <c>StorageHelper</c> con el bucket de adjuntos explícito. El <c>Mock&lt;IStorageFileApi&gt;</c>
    /// se inyecta en el ctor: es la UNICA vía por la que el helper alcanza el Storage.
    /// </summary>
    private StorageHelper CrearHelper()
    {
        var opciones = new SupabaseStorageOptions
        {
            Url = "https://proyecto.supabase.co",
            ServiceKey = "service-role-de-prueba",
            LogoBucket = "logos-tenant",
            EntregablesBucket = Bucket
        };

        // Orden de la v3: (bucket, path, content, options, ct) → el resultado es un RECORD
        _api.Setup(a => a.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<FileUploadOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Stream, FileUploadOptions?, CancellationToken>(
                (bucket, path, stream, opciones2, ct) =>
                {
                    _flujoRecibido = stream;
                    _opcionesRecibidas = opciones2;

                    // El helper envuelve los bytes en un `using var MemoryStream`: cuando
                    // SubirArchivoAsync devuelve, ese Stream YA ESTÁ DISPUESTO. La copia se hace
                    // AQUÍ, dentro del callback, que es el único momento en que sigue vivo.
                    using var copia = new MemoryStream();
                    if (stream.CanSeek) stream.Position = 0;
                    stream.CopyTo(copia);
                    _bytesRecibidos = copia.ToArray();
                })
            .ReturnsAsync((string bucket, string path, Stream content, FileUploadOptions? opciones, CancellationToken ct) =>
                new StorageUploadResult(path, content.CanSeek ? content.Length : 0, "\"etag-de-prueba\""));

        _api.Setup(a => a.CreateSignedUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://proyecto.supabase.co/storage/v1/object/sign/entregables/firmada?token=abc");

        _api.Setup(a => a.RemoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new StorageHelper(opciones, _api.Object);
    }

    // ─────────────────────── 58 · SubirArchivoAsync · BLOQUEO 3 + 8 ─────────────────────────

    [Fact]
    public async Task SubirArchivoAsync_BucketEntregablesYContentDisposition_InvocaStorageFileApiConEsosParametros()
    {
        // Arrange
        var sut = CrearHelper();
        var ruta = $"{TenantId}/{CicloId}/{AccionId}/2f1c9b0e-1111-2222-3333-444455556666.pdf";
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 contenido de prueba");

        // Act: se pasa el TIPO CANÓNICO (no un string de content-disposition) — enmienda 2
        var resultado = await sut.SubirArchivoAsync(Bucket, ruta, bytes, TipoArchivo.Pdf, CancellationToken.None);

        // Assert: bucket, ruta, el Stream con los bytes y un FileUploadOptions con el content-disposition
        _api.Verify(a => a.UploadAsync(
            Bucket, ruta,
            It.IsAny<Stream>(),
            It.Is<FileUploadOptions>(o => o.ContentDisposition == "inline"),
            It.IsAny<CancellationToken>()), Times.Once);

        // BLOQUEO 8: el helper DECIDE (TipoArchivoHelper.ContentDisposition(TipoArchivo)), sin
        // filename= (el nombre visible es la columna nombre_archivo y la ruta va con UUID) y con
        // Upsert = false (el uuid de la ruta garantiza unicidad: sobrescribir sería corrupción).
        Assert.NotNull(_opcionesRecibidas);
        Assert.Equal("inline", _opcionesRecibidas!.ContentDisposition);            // Pdf → inline
        Assert.DoesNotContain("filename=", _opcionesRecibidas.ContentDisposition ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(_opcionesRecibidas.Upsert);

        // El helper convierte byte[] → Stream, así que el contenido llega íntegro (no un Stream vacío).
        // Se compara contra la copia tomada DENTRO del mock: al volver, el Stream ya está disposeado
        // por el `using` del helper, y leerlo ahí lanzaría ObjectDisposedException.
        Assert.NotNull(_flujoRecibido);
        Assert.NotNull(_bytesRecibidos);
        Assert.Equal(bytes, _bytesRecibidos!);

        // Y devuelve el path persistido, colapsando el StorageUploadResult a string (mismo contrato
        // que SubirLogoAsync, ADR-013): null = el Storage no confirmó la subida (señal D-H).
        Assert.Equal(ruta, resultado);
    }

    // ─────────────────────── 59 · ObtenerUrlFirmadaAsync · TimeSpan ────────────────────────

    // 58-bis · Regresión HU-022-hotfix v2 (defecto I): la ruta devuelta por el SDK de Storage
    // llega CON el nombre del bucket delante ("entregables/tenant/..."), porque es la clave que
    // devuelve la API de Supabase. Si esa clave se persiste tal cual en file_path, la descarga
    // posterior busca "entregables/entregables/..." dentro del bucket "entregables" y Supabase
    // responde «Object not found»: el adjunto se listaba pero era imposible de descargar ni de
    // borrar (la compensación de RNF-014 tampoco encontraba el objeto y lo dejaba huérfano).
    // Las rutas del dominio son RELATIVAS a la raíz del bucket (ADR-013), así que el prefijo
    // debe retirarse en el helper, que es el único que habla con el SDK.
    [Fact]
    public async Task SubirArchivoAsync_ElSdkDevuelveLaClaveConElBucket_PersisteLaRutaRelativaAlBucket()
    {
        // Arrange: el SDK responde la clave con el prefijo del bucket, como hace Supabase.
        // El setup va DESPUÉS de CrearHelper() porque este reinicia los mocks del _api.
        var sut = CrearHelper();
        _api.Setup(a => a.UploadAsync(
                Bucket,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<FileUploadOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string bucket, string path, Stream content, FileUploadOptions? opciones, CancellationToken ct) =>
                new StorageUploadResult($"{bucket}/{path}", content.Length, "\"etag\""));
        var ruta = $"{TenantId}/{CicloId}/{AccionId}/2f1c9b0e-1111-2222-3333-444455556666.pdf";

        // Act
        var resultado = await sut.SubirArchivoAsync(Bucket, ruta, Encoding.UTF8.GetBytes("%PDF-1.7"), TipoArchivo.Pdf, CancellationToken.None);

        // Assert: lo que se persiste es la ruta RELATIVA, igual a la que se pidió subir.
        Assert.Equal(ruta, resultado);
        Assert.DoesNotContain($"{Bucket}/{Bucket}/", resultado!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObtenerUrlFirmadaAsync_Expiracion24h_LlamaCreateSignedUrlCon86400()
    {
        // Arrange
        var sut = CrearHelper();
        var ruta = $"{TenantId}/{CicloId}/{AccionId}/2f1c9b0e-1111-2222-3333-444455556666.pdf";

        // Act
        var url = await sut.ObtenerUrlFirmadaAsync(Bucket, ruta, 24, CancellationToken.None);

        // Assert: 24 h se traducen a TimeSpan.FromHours(24) (= 86.400 s). El dominio habla en HORAS
        // (ADR-013) y la capa de bajo nivel en TimeSpan; la conversión a segundos la hace el SDK.
        // Y SIN contentDisposition: esa es la enmienda 3 de ADR-013 (bloqueo 8).
        _api.Verify(a => a.CreateSignedUrlAsync(
            Bucket, ruta, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("https://proyecto.supabase.co/storage/v1/object/sign/entregables/firmada?token=abc", url);
    }

    // ─────────────────────── 60 · EliminarArchivoAsync ────────────────────────────────────

    [Fact]
    public async Task EliminarArchivoAsync_InvoaRemoveConLaRutaEnElBucketCorrecto()
    {
        // Arrange
        var sut = CrearHelper();
        var ruta = $"{TenantId}/{CicloId}/{AccionId}/2f1c9b0e-1111-2222-3333-444455556666.pdf";

        // Act
        var ok = await sut.EliminarArchivoAsync(Bucket, ruta, CancellationToken.None);

        // Assert: RemoveAsync con el bucket y la ruta EXACTOS (nunca el nombre visible del archivo)
        // y su bool se PROPAGA como retorno (false = degradación elegante D-I, RNF-014)
        _api.Verify(a => a.RemoveAsync(Bucket, ruta, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(ok);

        // …y cuando el Storage reporta false, el helper NO lo maquilla como éxito
        _api.Setup(a => a.RemoveAsync(Bucket, ruta, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.False(await sut.EliminarArchivoAsync(Bucket, ruta, CancellationToken.None));
    }

    // ─────────────────────── 61 · Bucket por defecto ──────────────────────────────────────

    [Fact]
    public async Task StorageHelper_BucketPorDefecto_EsEntregables()
    {
        // Arrange: SupabaseStorageOptions SIN EntregablesBucket explícito → el default del POCO
        var opciones = new SupabaseStorageOptions { Url = "https://proyecto.supabase.co", ServiceKey = "k" };
        _api.Setup(a => a.CreateSignedUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://x.supabase.co/firmada");
        var sut = new StorageHelper(opciones, _api.Object);
        const string ruta = "c/acto/u.pdf";

        // Act
        await sut.ObtenerUrlFirmadaAsync(Bucket, ruta, 24, CancellationToken.None);

        // Assert: el default es "entregables", igual que el LogoBucket por defecto de ADR-005
        Assert.Equal("entregables", opciones.EntregablesBucket);
        _api.Verify(a => a.CreateSignedUrlAsync(
            "entregables", ruta, TimeSpan.FromHours(24), It.IsAny<CancellationToken>()), Times.Once);

        // Y el default del logo NO se contamina: sigue siendo "logos-tenant" (HU-006 intacta)
        Assert.Equal("logos-tenant", opciones.LogoBucket);
    }
}
