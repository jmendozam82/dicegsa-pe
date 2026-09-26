// PE-GOL SaaS — Planes de Suscripción (HU-002 § UI)
// Modal de confirmación eliminar (D-2: DELETE físico; form POST, navegación MVC full page load).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalConfirmarEliminar');
    if (!modalEl) {
        return; // La vista no incluye el modal (p. ej. Index sin datos no lo necesita).
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var planId = boton.getAttribute('data-plan-id');
        var planNombre = boton.getAttribute('data-plan-nombre');

        document.getElementById('modalEliminarTitulo').textContent = 'Eliminar plan';
        document.getElementById('modalEliminarTexto').textContent =
            "¿Eliminar el plan '" + planNombre + "'? Esta acción es permanente y no se puede deshacer.";
        document.getElementById('formEliminar').setAttribute('action', '/Planes/Eliminar/' + planId);
    });

    // Si falla (ej: plan con tenants asociados), el servidor vuelve al Index con TempData["Error"].
});