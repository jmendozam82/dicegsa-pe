namespace PE_GOL.Utility.Storage;

/// <summary>
/// Opciones de la subida (ADR-013, spec HU-022 v3 — bloqueo 3).
/// <para>
/// <b>Deliberadamente NO expone el <c>FileOptions</c> del SDK de Supabase</b>: el cliente de
/// Storage no debe filtrarse a las capas superiores (D-O). Solo viajan las tres claves que
/// el dominio necesita, y son las mismas para el logo (HU-006) y para los entregables (HU-022),
/// que es justo lo que permite una única abstracción de Storage.
/// </para>
/// <para>
/// Se declara como <c>record</c> posicional: las opciones de una subida no tienen identidad,
/// se comparan por valor, y el compilador genera el Deconstruct para no repetir los parámetros
/// opcionales en las 3 llamadas (subida de adjuntos, subida de logo y logo heredado).
/// </para>
/// </summary>
/// <param name="ContentType">
/// MIME canónico de la allowlist (<c>application/pdf</c>, <c>image/png</c>…), <b>nunca</b> el
/// <c>ContentType</c> crudo que envía el navegador. <c>null</c> = que lo marque el SDK.
/// </param>
/// <param name="ContentDisposition">
/// <c>"inline"</c> o <c>"attachment"</c>, <b>sin <c>filename=</c></b>. Lo rellena
/// <c>StorageHelper.SubirArchivoAsync</c> a partir de <c>TipoArchivoHelper.ContentDisposition(tipo)</c>
/// (D-P, bloqueo 8): decide por el <b>tipo canónico detectado</b>, no por el nombre, de modo que
/// un <c>ContentType</c> del navegador no puede influir en cómo se sirve el objeto.
/// El nombre visible es la columna <c>nombre_archivo</c> (saneada, D-E) y la ruta va con UUID.
/// </param>
/// <param name="Upsert">
/// <c>false</c> por defecto: el <c>uuid</c> de la ruta garantiza unicidad y sobrescribir un
/// adjunto existente sería corrupción de datos. Es la semántica <b>opuesta</b> al
/// <c>Upsert = true</c> deliberado de <c>SubirLogoAsync</c> (D4/ADR-005: reemplazar el logo no
/// debe dejar huérfanos).
/// </param>
public sealed record FileUploadOptions(
    string? ContentType = null,
    string? ContentDisposition = null,
    bool Upsert = false);
