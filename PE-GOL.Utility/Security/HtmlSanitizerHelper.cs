using System.Text.RegularExpressions;

namespace PE_GOL.Utility.Security;

/// <summary>
/// Sanitización de texto enriquecido (Spec HU-011 § Sanitización HTML — D-C).
/// Política de allowlist estricta SIN librería externa (HtmlSanitizer de Ganss no está en el
/// stack de AGENTS.md § 1 → requeriría ADR; se evita con helper propio, enmarcado en SEC-05):
///  · Etiquetas permitidas: p, br, b, strong, i, em, ul, ol, li (formato básico del CA #1).
///  · Atributos: NINGUNO — se eliminan todos (onclick, style, class, href, src → XSS por atributo).
///  · Etiquetas eliminadas CON su contenido: script, style, iframe, object, embed, form, input,
///    button, a (enlaces no permitidos en v1.0 — sin atributos no tienen sentido).
///  · Etiquetas desconocidas: se elimina la etiqueta pero se conserva el texto interior
///    (p. ej. &lt;div&gt;texto&lt;/div&gt; → texto).
///  · Comentarios HTML &lt;!-- --&gt; eliminados.
///  · Entidades básicas (&amp;amp;, &amp;lt;, &amp;gt;) conservadas.
///  · Colapso de espacios + trim.
/// StripHtml(): quita todas las etiquetas y decodifica las entidades básicas → texto visible
/// (usado por la BLL para la validación CA #2: el texto visible no puede quedar vacío).
/// </summary>
public static class HtmlSanitizerHelper
{
    private static readonly HashSet<string> EtiquetasPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "b", "strong", "i", "em", "ul", "ol", "li"
    };

    private static readonly Regex ComentarioRegex = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>script/style/iframe/object/embed/form/button/a: se eliminan CON su contenido
    /// (cerradas o sin cerrar — XSS, SEC-05). Backreference \1 = etiqueta de cierre.</summary>
    private static readonly Regex BloquePeligrosoRegex = new(
        @"<(script|style|iframe|object|embed|form|button|a)\b[^>]*>(?:.*?</\1>|.*)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>input es void element (sin cierre): se elimina solo la etiqueta.</summary>
    private static readonly Regex EtiquetaVoidPeligrosaRegex = new(
        @"<input\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Captura cualquier etiqueta restante: nombre + atributos (que se descartan).</summary>
    private static readonly Regex TagRegex = new(
        @"</?([a-zA-Z][a-zA-Z0-9]*)(?:\s[^>]*)?>",
        RegexOptions.Compiled);

    private static readonly Regex EspaciosRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Sanitiza el HTML según la política D-C (allowlist, sin atributos, sin bloques
    /// peligrosos, sin comentarios, colapso de espacios). El resultado es lo que se persiste.</summary>
    public static string SanitizarHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var sinComentarios = ComentarioRegex.Replace(html, string.Empty);
        var sinVoid = EtiquetaVoidPeligrosaRegex.Replace(sinComentarios, string.Empty);
        var sinBloques = BloquePeligrosoRegex.Replace(sinVoid, string.Empty);

        // Allowlist: las permitidas se conservan SIN atributos; las desconocidas se eliminan
        // conservando el texto interior (p. ej. <div>texto</div> → texto).
        var sanitizado = TagRegex.Replace(sinBloques, m =>
        {
            var nombre = m.Groups[1].Value;
            if (!EtiquetasPermitidas.Contains(nombre)) return string.Empty;
            return m.Value.StartsWith("</", StringComparison.Ordinal) ? $"</{nombre}>" : $"<{nombre}>";
        });

        return EspaciosRegex.Replace(sanitizado, " ").Trim();
    }

    /// <summary>Quita todas las etiquetas y decodifica las entidades básicas → texto visible.
    /// Usado por la BLL para la validación CA #2 (el texto visible no puede quedar vacío).</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var sinTags = Regex.Replace(html, @"<[^>]+>", string.Empty);
        return sinTags
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Trim();
    }
}