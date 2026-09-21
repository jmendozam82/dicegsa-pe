// PE-GOL SaaS — Filosofía / Valores Corporativos (HU-012)
// Lista dinámica de valores: agregar, eliminar, reordenar (↑/↓) y re-indexado
// de los inputs name="Valores[i]" (D-3: form POST, navegación MVC full page load).
// Validación jQuery Validate (UX-04): requerido, máx 100 chars, sin duplicados
// case-insensitive, máx 15 valores (RN-008). El servidor (FluentValidation) es
// siempre la fuente de verdad.
(function ($) {
    'use strict';

    var MAX_VALORES = 15;

    function contarValores() {
        return $('#listaValores .fila-valor').length;
    }

    // Re-indexa badges e inputs tras agregar/eliminar/reordenar.
    function reindexar() {
        $('#listaValores .fila-valor').each(function (i) {
            var $fila = $(this);
            $fila.find('.badge').text(i + 1);
            $fila.find('.input-valor').attr('name', 'Valores[' + i + ']');
        });
        $('#estadoVacioValores').toggle(contarValores() === 0);
        $('#btnAgregarValor').prop('disabled', contarValores() >= MAX_VALORES);
    }

    // Aplica las reglas de jQuery Validate a un input de valor (incluye los dinámicos).
    function aplicarReglas($input) {
        $input.rules('add', {
            required: true,
            maxlength: 100,
            valorUnico: true
        });
    }

    function crearFila(valor) {
        var $fila = $('<div>', { class: 'row g-2 mb-2 align-items-center fila-valor' });

        $fila.append(
            $('<div>', { class: 'col-auto' }).append(
                $('<span>', { class: 'badge text-bg-secondary' })
            )
        );

        $fila.append(
            $('<div>', { class: 'col' }).append(
                $('<input>', {
                    type: 'text',
                    class: 'form-pe-input input-valor',
                    name: 'Valores[0]',
                    maxlength: 100,
                    placeholder: 'Ej: Liderazgo',
                    value: valor || ''
                })
            )
        );

        var $acciones = $('<div>', { class: 'col-auto d-flex gap-1' });
        $acciones.append(
            $('<button>', { type: 'button', class: 'btn-pe btn-pe--secondary btn-pe--sm btn-subir', title: 'Subir' })
                .append($('<i>', { class: 'bi bi-arrow-up' }))
        );
        $acciones.append(
            $('<button>', { type: 'button', class: 'btn-pe btn-pe--secondary btn-pe--sm btn-bajar', title: 'Bajar' })
                .append($('<i>', { class: 'bi bi-arrow-down' }))
        );
        $acciones.append(
            $('<button>', { type: 'button', class: 'btn-pe btn-pe--danger btn-pe--sm btn-quitar', title: 'Eliminar' })
                .append($('<i>', { class: 'bi bi-trash' }))
        );
        $fila.append($acciones);

        return $fila;
    }

    $(function () {
        // Validador custom: sin duplicados (case-insensitive) entre los valores de la lista.
        $.validator.addMethod('valorUnico', function (value, element) {
            var normalizado = $.trim(value).toLowerCase();
            if (!normalizado) {
                return true;
            }
            var duplicado = false;
            $('#listaValores .input-valor').each(function () {
                if (this !== element && $.trim($(this).val()).toLowerCase() === normalizado) {
                    duplicado = true;
                    return false;
                }
            });
            return !duplicado;
        }, 'Este valor ya existe en la lista.');

        $('#formValores').validate({
            errorElement: 'span',
            errorClass: 'form-pe-error',
            errorPlacement: function (error, element) {
                error.insertAfter(element.closest('.col'));
            }
        });

        // Reglas iniciales sobre las filas renderizadas por el servidor.
        $('#listaValores .input-valor').each(function () {
            aplicarReglas($(this));
        });

        $('#btnAgregarValor').on('click', function () {
            if (contarValores() >= MAX_VALORES) {
                return;
            }
            var $fila = crearFila('');
            $('#listaValores').append($fila);
            aplicarReglas($fila.find('.input-valor'));
            reindexar();
            $fila.find('.input-valor').trigger('focus');
        });

        $('#listaValores').on('click', '.btn-quitar', function () {
            $(this).closest('.fila-valor').remove();
            reindexar();
        });

        $('#listaValores').on('click', '.btn-subir', function () {
            var $fila = $(this).closest('.fila-valor');
            var $anterior = $fila.prev('.fila-valor');
            if ($anterior.length) {
                $fila.insertBefore($anterior);
                reindexar();
            }
        });

        $('#listaValores').on('click', '.btn-bajar', function () {
            var $fila = $(this).closest('.fila-valor');
            var $siguiente = $fila.next('.fila-valor');
            if ($siguiente.length) {
                $fila.insertAfter($siguiente);
                reindexar();
            }
        });

        reindexar();
    });
})(jQuery);