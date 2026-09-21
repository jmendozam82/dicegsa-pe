// PE-GOL SaaS — Tenants (HU-045 Tanda A.2)
// Modal de confirmación activar/desactivar (D-3: form POST, navegación MVC full page load).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalConfirmarAccion');
    if (!modalEl) {
        return; // La vista no incluye el modal (p. ej. Index sin datos no lo necesita).
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var tenantId = boton.getAttribute('data-tenant-id');
        var tenantNombre = boton.getAttribute('data-tenant-nombre');
        var accion = boton.getAttribute('data-accion'); // 'activar' | 'desactivar'
        var esActivar = accion === 'activar';

        var titulo = document.getElementById('modalConfirmarTitulo');
        var texto = document.getElementById('modalConfirmarTexto');
        var formulario = document.getElementById('formConfirmarAccion');
        var botonConfirmar = document.getElementById('modalConfirmarBoton');

        if (esActivar) {
            titulo.textContent = 'Activar tenant';
            texto.textContent = "¿Activar el tenant '" + tenantNombre + "'? Sus usuarios deberán volver a iniciar sesión.";
            formulario.setAttribute('action', '/Tenants/Activar/' + tenantId);
            botonConfirmar.textContent = 'Activar';
            botonConfirmar.classList.remove('btn-pe--danger');
            botonConfirmar.classList.add('btn-pe--primary');
        } else {
            titulo.textContent = 'Desactivar tenant';
            texto.textContent = "¿Desactivar el tenant '" + tenantNombre + "'? Sus usuarios no podrán iniciar sesión y sus sesiones activas se invalidarán.";
            formulario.setAttribute('action', '/Tenants/Desactivar/' + tenantId);
            botonConfirmar.textContent = 'Desactivar';
            botonConfirmar.classList.remove('btn-pe--primary');
            botonConfirmar.classList.add('btn-pe--danger');
        }
    });
});