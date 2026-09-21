// PE-GOL SaaS — Pilares Estratégicos (HU-013)
// Modal de confirmación eliminar (D-3: form POST, navegación MVC full page load).
// Mismo patrón que areas.js (HU-009): data-* en el botón disparador + action dinámico.
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalEliminarPilar');
    if (!modalEl) {
        return; // La vista no incluye el modal.
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var pilarId = boton.getAttribute('data-pilar-id');
        var pilarNombre = boton.getAttribute('data-pilar-nombre');
        var pilarCodigo = boton.getAttribute('data-pilar-codigo');
        var cicloId = boton.getAttribute('data-ciclo-id');

        var texto = document.getElementById('modalEliminarTexto');
        var formulario = document.getElementById('formEliminarPilar');

        texto.textContent = "¿Eliminar el pilar '" + pilarCodigo + ' — ' + pilarNombre +
            "'? Esta acción no se puede deshacer. Si el pilar tiene objetivos CG u OKRs asociados, el sistema lo impedirá.";
        formulario.setAttribute('action', '/Pilar/Eliminar/' + pilarId + '?cicloId=' + encodeURIComponent(cicloId));
    });
});