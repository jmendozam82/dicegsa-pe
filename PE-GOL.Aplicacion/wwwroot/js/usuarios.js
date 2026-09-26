// PE-GOL SaaS — Usuarios (HU-003 § UI del SA)
// Modal de confirmación activar/desactivar/resetear contraseña (form POST, MVC full page load).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalConfirmarAccion');
    if (!modalEl) {
        return;
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var usuarioId = boton.getAttribute('data-usuario-id');
        var usuarioNombre = boton.getAttribute('data-usuario-nombre');
        var accion = boton.getAttribute('data-accion'); // 'activar' | 'desactivar' | 'reset'

        var titulo = document.getElementById('modalConfirmarTitulo');
        var texto = document.getElementById('modalConfirmarTexto');
        var formulario = document.getElementById('formConfirmarAccion');
        var botonConfirmar = document.getElementById('modalConfirmarBoton');

        if (accion === 'activar') {
            titulo.textContent = 'Activar usuario';
            texto.textContent = "¿Activar al usuario '" + usuarioNombre + "'? Podrá iniciar sesión nuevamente.";
            formulario.setAttribute('action', '/Usuarios/Activar/' + usuarioId);
            botonConfirmar.textContent = 'Activar';
            botonConfirmar.classList.remove('btn-pe--danger');
            botonConfirmar.classList.add('btn-pe--primary');
        } else if (accion === 'desactivar') {
            titulo.textContent = 'Desactivar usuario';
            texto.textContent = "¿Desactivar al usuario '" + usuarioNombre + "'? Se revocan sus sesiones activas y no podrá iniciar sesión.";
            formulario.setAttribute('action', '/Usuarios/Desactivar/' + usuarioId);
            botonConfirmar.textContent = 'Desactivar';
            botonConfirmar.classList.remove('btn-pe--primary');
            botonConfirmar.classList.add('btn-pe--danger');
        } else {
            titulo.textContent = 'Resetear contraseña';
            texto.textContent = "¿Generar una nueva contraseña temporal para '" + usuarioNombre + "'? Deberá cambiarla en su próximo inicio de sesión.";
            formulario.setAttribute('action', '/Usuarios/ResetContrasena/' + usuarioId);
            botonConfirmar.textContent = 'Resetear';
            botonConfirmar.classList.remove('btn-pe--primary');
            botonConfirmar.classList.add('btn-pe--danger');
        }
    });
});