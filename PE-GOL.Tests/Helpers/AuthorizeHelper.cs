using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace PE_GOL.Tests.Helpers;

/// <summary>
/// Helper de reflexión para los tests de humo de autorización (HU-045, Tanda T2).
/// Extrae los roles del atributo [Authorize(Roles = ...)] a nivel de clase y de acción
/// (doble capa MVC + API, ARCH-04). Si el atributo no tiene Roles (p. ej. [Authorize] a
/// secas = cualquier usuario autenticado), devuelve una lista vacía.
/// </summary>
public static class AuthorizeHelper
{
    /// <summary>Roles del [Authorize(Roles)] a nivel de clase. Vacío si no hay Roles.</summary>
    public static string[] RolesDeClase(Type controllerType)
        => RolesDe(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true));

    /// <summary>Roles del [Authorize(Roles)] de la acción indicada. Vacío si no hay Roles.</summary>
    public static string[] RolesDeAccion(Type controllerType, string actionName)
    {
        var metodo = controllerType.GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        return metodo is null
            ? []
            : RolesDe(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    /// <summary>true si la clase tiene [Authorize] (con o sin Roles).</summary>
    public static bool TieneAuthorizeClase(Type controllerType)
        => controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any();

    /// <summary>true si la acción tiene [Authorize] (con o sin Roles).</summary>
    public static bool TieneAuthorizeAccion(Type controllerType, string actionName)
    {
        var metodo = controllerType.GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        return metodo is not null &&
               metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any();
    }

    private static string[] RolesDe(IEnumerable<object> atributos)
    {
        var attr = atributos.OfType<AuthorizeAttribute>().FirstOrDefault();
        return string.IsNullOrWhiteSpace(attr?.Roles)
            ? []
            : attr.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}