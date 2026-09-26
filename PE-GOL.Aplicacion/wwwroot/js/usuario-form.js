// PE-GOL SaaS — Formulario Usuario (HU-003 § UI del SA)
// Valida el formulario y togglea el campo Área según rol (solo JefeArea lo requiere).
$(function () {
    'use strict';

    var rol = $('#rol');
    var campoArea = $('#campoArea');
    var areaIdInput = $('#areaId');

    // RN-011: areaId solo aplica a JefeArea. Deshabilitar el input (un campo disabled no se
    // envía en el POST → evita que el autofill del navegador lo llene y rompa el binding).
    function aplicarRol() {
        var esJefeArea = rol.val() === 'JefeArea';
        campoArea.toggle(esJefeArea);
        if (!esJefeArea) {
            areaIdInput.prop('disabled', true).val('');
        } else {
            areaIdInput.prop('disabled', false);
        }
    }

    rol.on('change', aplicarRol);
    aplicarRol();

    var reglas = {
        nombre: { required: true, maxlength: 150 },
        correo: { required: true, email: true, maxlength: 200 },
        tenantId: { required: true },
        rol: { required: true },
        areaId: {
            required: function () { return rol.val() === 'JefeArea'; },
            pattern: /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
        }
    };
    var mensajes = {
        nombre: {
            required: 'El nombre es obligatorio.',
            maxlength: 'El nombre no puede superar 150 caracteres.'
        },
        correo: {
            required: 'El correo es obligatorio.',
            email: 'Ingrese un correo válido.',
            maxlength: 'El correo no puede superar 200 caracteres.'
        },
        tenantId: {
            required: 'Seleccione un tenant.'
        },
        rol: {
            required: 'Seleccione un rol.'
        },
        areaId: {
            required: 'El rol JefeArea requiere un área.',
            pattern: 'Ingrese el GUID del área con formato válido (8-4-4-4-12).'
        }
    };

    // Solo en alta existe el campo password (en edición se usa reset-contrasena).
    if ($('#password').length) {
        reglas.password = { required: true, minlength: 8, maxlength: 128 };
        mensajes.password = {
            required: 'La contraseña temporal es obligatoria.',
            minlength: 'Debe tener al menos 8 caracteres.',
            maxlength: 'No puede superar 128 caracteres.'
        };
    }

    $('#formUsuario').validate({
        rules: reglas,
        messages: mensajes,
        errorElement: 'span',
        errorClass: 'form-pe-error',
        errorPlacement: function (error, element) {
            error.insertAfter(element);
        }
    });
});