// PE-GOL SaaS — Responsables (HU-010)
// Modal de confirmación desactivar (D-3: form POST, navegación MVC full page load).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalConfirmarAccion');
    if (!modalEl) {
        return; // La vista no incluye el modal.
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var responsableId = boton.getAttribute('data-responsable-id');
        var responsableNombre = boton.getAttribute('data-responsable-nombre');
        var cicloId = boton.getAttribute('data-ciclo-id');

        var titulo = document.getElementById('modalConfirmarTitulo');
        var texto = document.getElementById('modalConfirmarTexto');
        var formulario = document.getElementById('formConfirmarAccion');
        var botonConfirmar = document.getElementById('modalConfirmarBoton');

        titulo.textContent = 'Desactivar responsable';
        texto.textContent = "¿Desactivar al responsable '" + responsableNombre + "'? Su área quedará sin responsable y deberá reasignarse.";
        formulario.setAttribute('action', '/Responsables/Desactivar/' + responsableId + '?cicloId=' + encodeURIComponent(cicloId));
        botonConfirmar.textContent = 'Desactivar';
        botonConfirmar.classList.remove('btn-pe--primary');
        botonConfirmar.classList.add('btn-pe--danger');
    });
});