// PE-GOL SaaS — Acciones del Plan (HU-019)
// Modal de confirmación eliminar (D-3: form POST, navegación MVC full page load).
// Mismo patrón que pilares.js/areas.js: data-* en el botón disparador + action dinámico.
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalEliminarAccion');
    if (!modalEl) {
        return; // La vista no incluye el modal.
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var accionId = boton.getAttribute('data-accion-id');
        var accionCodigo = boton.getAttribute('data-accion-codigo');
        var objetivoCgId = boton.getAttribute('data-objetivo-cg-id');

        var texto = document.getElementById('modalEliminarAccionTexto');
        var formulario = document.getElementById('formEliminarAccion');

        texto.textContent = "¿Eliminar la acción '" + accionCodigo +
            "'? Esta acción no se puede deshacer. El historial de progreso y los entregables asociados se eliminarán en cascada.";
        formulario.setAttribute('action', '/AccionPlan/Eliminar/' + accionId +
            '?objetivoCgId=' + encodeURIComponent(objetivoCgId));
    });
});