// PE-GOL SaaS — Registro mensual de valores reales de KRs (HU-026)
// Edición en línea sobre la grilla que el MVC ya renderizó (ADR-016: el navegador nunca ve
// la API interna ni el JWT; los proxies /ValorMensualKr/Guardar y /ValorMensualKr/EliminarValor
// adjuntan el token server-side). Validación de cliente con jQuery Validate (UX-04); la BLL
// sigue siendo la fuente de verdad y rechaza lo que la UI deje pasar.
$(function () {
    'use strict';

    var grilla = document.getElementById('grillaOkr');
    if (!grilla) {
        return; // La vista no incluye la grilla (OKR inexistente o sin KRs).
    }

    var okrId = grilla.getAttribute('data-okr-id');
    var formulario = document.getElementById('formGrilla');
    var contenedorAvisos = document.getElementById('avisosInline');
    var modalEliminarEl = document.getElementById('modalEliminarValor');
    var pendienteEliminar = null;

    // ─── Utilidades de formato ────────────────────────────────────────────────

    function num(valor, decimales) {
        var n = Number(valor);
        return isFinite(n) ? n.toFixed(decimales) : (0).toFixed(decimales);
    }

    function claseSemaforo(semaforo) {
        switch ((semaforo || '').trim().toLowerCase()) {
            case 'verde': return 'verde';
            case 'amarillo': return 'amarillo';
            case 'rojo': return 'rojo';
            default: return 'gris';
        }
    }

    // ─── Avisos (alert del Design System; nunca alert() nativo) ───────────────

    function mostrarAviso(tipo, texto) {
        if (!contenedorAvisos) {
            return;
        }
        var icono = tipo === 'success' ? 'bi-check-circle'
            : tipo === 'info' ? 'bi-info-circle'
                : 'bi-exclamation-triangle';

        var alerta = document.createElement('div');
        alerta.className = 'alert alert-' + tipo + ' alert-dismissible fade show';
        alerta.setAttribute('role', 'alert');

        var ico = document.createElement('i');
        ico.className = 'bi ' + icono;
        ico.setAttribute('aria-hidden', 'true');
        alerta.appendChild(ico);

        // textContent: los mensajes de la BLL nunca se inyectan como HTML (SEC-05 · XSS).
        var mensaje = document.createElement('span');
        mensaje.textContent = ' ' + texto;
        alerta.appendChild(mensaje);

        var cerrar = document.createElement('button');
        cerrar.type = 'button';
        cerrar.className = 'btn-close';
        cerrar.setAttribute('data-bs-dismiss', 'alert');
        cerrar.setAttribute('aria-label', 'Cerrar');
        alerta.appendChild(cerrar);

        contenedorAvisos.innerHTML = '';
        contenedorAvisos.appendChild(alerta);

        if (tipo === 'success' || tipo === 'info') {
            window.setTimeout(function () {
                if (alerta.parentNode) {
                    alerta.remove();
                }
            }, 6000);
        }
    }

    function extraerErrores(datos, porDefecto) {
        if (!datos) {
            return [porDefecto];
        }
        var mensajes = [];
        if (datos.errores && datos.errores.length) {
            mensajes = datos.errores;
        } else if (datos.message) {
            mensajes = [datos.message];
        } else if (datos.errors) {
            Object.keys(datos.errors).forEach(function (clave) {
                (datos.errors[clave] || []).forEach(function (m) { mensajes.push(m); });
            });
        }
        return mensajes.length ? mensajes : [porDefecto];
    }

    function leerRespuesta(response) {
        return response.json()
            .then(function (datos) { return { ok: response.ok, datos: datos }; })
            .catch(function () { return { ok: false, datos: null }; });
    }

    // ─── Validación de cliente (UX-04) ────────────────────────────────────────

    // CA #4: escala 0.0–1.0 con un decimal. Vacío significa "mes sin registro", no 0.
    if ($.validator && !$.validator.methods.valorEscala) {
        $.validator.addMethod('valorEscala', function (value) {
            return /^(0(\.\d)?|1(\.0)?)$/.test($.trim(value));
        }, 'Ingrese un valor entre 0.0 y 1.0 con un decimal (ej. 0.75).');
    }

    var validator = $(formulario).validate({
        errorElement: 'span',
        errorClass: 'form-pe-error',
        errorPlacement: function (error, element) {
            error.insertAfter(element);
        }
    });

    $('.valor-mes').each(function () {
        $(this).rules('add', { valorEscala: true });
    });

    // ─── Pintado de la fila recalculada (F3: sin recargar) ────────────────────

    function alternarBotonEliminar($celda, registrado, mes, nombre) {
        var $boton = $celda.find('.btn-eliminar-valor');
        if (registrado) {
            if (!$boton.length) {
                $boton = $('<button type="button" class="btn-pe btn-pe--danger btn-pe--sm btn-eliminar-valor"></button>')
                    .attr('data-bs-toggle', 'modal')
                    .attr('data-bs-target', '#modalEliminarValor')
                    .attr('data-mes', mes)
                    .attr('data-mes-nombre', nombre || '')
                    .attr('title', 'Eliminar el valor de ' + (nombre || mes));
                $boton.append($('<i class="bi bi-trash"></i>').attr('aria-hidden', 'true'));
                $celda.append($boton);
            }
        } else if ($boton.length) {
            $boton.remove();
        }
    }

    function pintarFila($fila, keyResult) {
        (keyResult.meses || []).forEach(function (celda) {
            var $celda = $fila.find('.celda-mes').eq(celda.mes - 1);
            if (!$celda.length) {
                return;
            }

            if (celda.editable) {
                var $input = $celda.find('.valor-mes');
                if ($input.length) {
                    $input.val(celda.valor === null || celda.valor === undefined ? '' : num(celda.valor, 1));
                    $input.attr('data-registrado', celda.registrado ? 'true' : 'false');
                    $input.removeClass('is-invalid');
                    $input.nextAll('.form-pe-error').remove();
                }
                alternarBotonEliminar($celda, celda.registrado, celda.mes, celda.nombre);
            } else {
                // Mes fuera de la ventana: texto, nunca input (CA #2/#3).
                $celda.empty();
                var muestra = (celda.registrado && celda.valor !== null && celda.valor !== undefined)
                    ? num(celda.valor, 1)
                    : '—';
                $celda.append(
                    $('<span class="text-secondary"></span>')
                        .attr('title', celda.motivoBloqueo || '')
                        .text(muestra));
            }
        });

        $fila.find('.valor-q1').text(num(keyResult.puntuacionQ1, 3));
        $fila.find('.valor-q2').text(num(keyResult.puntuacionQ2, 3));
        $fila.find('.valor-q3').text(num(keyResult.puntuacionQ3, 3));
        $fila.find('.valor-q4').text(num(keyResult.puntuacionQ4, 3));
        $fila.find('.valor-final').text(num(keyResult.puntuacionFinal, 3));
        $fila.find('.valor-ponderada').text(num(keyResult.puntuacionPonderada, 3));
        $fila.find('[data-semaforo]')
            .removeClass('semaforo--verde semaforo--amarillo semaforo--rojo semaforo--gris')
            .addClass('semaforo--' + claseSemaforo(keyResult.semaforo))
            .attr('data-semaforo', keyResult.semaforo)
            .text(keyResult.semaforo);
        $fila.attr('data-kr-mes-con-valor', keyResult.mesesConValor);
    }

    function pintarOkr(calculo) {
        if (!calculo) {
            return;
        }
        var final = $('#okrPuntuacionFinal');
        var semaforo = $('#okrSemaforo');
        var krs = $('#okrKrsConValor');

        if (final.length) {
            final.text(num(calculo.puntuacionFinal, 3));
        }
        if (semaforo.length) {
            semaforo
                .removeClass('semaforo--verde semaforo--amarillo semaforo--rojo semaforo--gris')
                .addClass('semaforo--' + claseSemaforo(calculo.semaforo))
                .attr('data-semaforo', calculo.semaforo)
                .text(calculo.semaforo);
        }
        if (krs.length) {
            var total = $('.tabla-pe tbody tr[data-kr-id]').length;
            krs.text(calculo.krsConValor + ' de ' + total);
        }
    }

    // ─── Guardar una fila ─────────────────────────────────────────────────────

    function guardarFila($fila) {
        var krId = $fila.attr('data-kr-id');
        var $boton = $fila.find('.btn-guardar-fila');
        var cuerpo = new URLSearchParams();
        var indice = 0;
        var valido = true;
        var algunoConValor = false;

        $fila.find('.valor-mes').each(function () {
            if (!validator.element(this)) {
                valido = false;
            }
            var valor = $.trim($(this).val());
            if (valor === '') {
                return;
            }
            algunoConValor = true;
            cuerpo.append('valores[' + indice + '].mes', $(this).attr('data-mes'));
            cuerpo.append('valores[' + indice + '].valor', valor.replace(',', '.'));
            indice += 1;
        });
        validator.showErrors();

        if (!valido) {
            mostrarAviso('warning', 'Revise los valores marcados: deben estar entre 0.0 y 1.0 con un decimal.');
            return;
        }
        if (!algunoConValor) {
            mostrarAviso('info', 'No hay valores nuevos que registrar. Escriba al menos un mes o use la papelera para eliminar uno existente.');
            return;
        }

        $boton.prop('disabled', true);
        fetch('/ValorMensualKr/Guardar/' + okrId + '/' + krId, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
            body: cuerpo.toString()
        })
            .then(leerRespuesta)
            .then(function (r) {
                if (!r.ok || !r.datos || !r.datos.keyResult) {
                    mostrarAviso('danger', extraerErrores(r.datos, 'No se pudieron registrar los valores. Intente de nuevo.').join(' '));
                    return;
                }
                pintarFila($fila, r.datos.keyResult);
                pintarOkr(r.datos.calculoOkr);
                mostrarAviso('success', 'Valores de ' + $fila.attr('data-kr-codigo') +
                    ' registrados. Puntuación final: ' + num(r.datos.keyResult.puntuacionFinal, 3) + '.');
            })
            .catch(function () {
                mostrarAviso('danger', 'Error de conexión al registrar los valores. Revise su sesión e intente de nuevo.');
            })
            .then(function () {
                $boton.prop('disabled', false);
            });
    }

    // ─── Eliminar el valor de un mes ─────────────────────────────────────────

    if (modalEliminarEl) {
        modalEliminarEl.addEventListener('show.bs.modal', function (event) {
            var boton = event.relatedTarget;
            if (!boton) {
                return;
            }
            var $fila = $(boton).closest('tr');
            pendienteEliminar = {
                boton: boton,
                fila: $fila,
                krId: $fila.attr('data-kr-id'),
                krCodigo: $fila.attr('data-kr-codigo'),
                mes: boton.getAttribute('data-mes'),
                mesNombre: boton.getAttribute('data-mes-nombre') || boton.getAttribute('data-mes')
            };
            document.getElementById('modalEliminarValorTexto').textContent =
                '¿Eliminar el valor de ' + pendienteEliminar.mesNombre + ' del Key Result ' +
                pendienteEliminar.krCodigo + '? El mes quedará sin registro y las puntuaciones se recalcularán.';
        });
    }

    var btnConfirmar = document.getElementById('btnConfirmarEliminarValor');
    if (btnConfirmar) {
        btnConfirmar.addEventListener('click', function () {
            if (!pendienteEliminar) {
                return;
            }
            var objetivo = pendienteEliminar;
            var cuerpo = new URLSearchParams();
            cuerpo.append('mes', objetivo.mes);
            $(btnConfirmar).prop('disabled', true);

            fetch('/ValorMensualKr/EliminarValor/' + okrId + '/' + objetivo.krId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: cuerpo.toString()
            })
                .then(leerRespuesta)
                .then(function (r) {
                    if (!r.ok || !r.datos || !r.datos.keyResult) {
                        mostrarAviso('danger', extraerErrores(r.datos, 'No se pudo eliminar el valor. Intente de nuevo.').join(' '));
                        return;
                    }
                    pintarFila(objetivo.fila, r.datos.keyResult);
                    pintarOkr(r.datos.calculoOkr);
                    if (modalEliminarEl && bootstrap.Modal.getInstance(modalEliminarEl)) {
                        bootstrap.Modal.getInstance(modalEliminarEl).hide();
                    }
                    mostrarAviso('success', 'Valor de ' + objetivo.mesNombre + ' eliminado de ' + objetivo.krCodigo + '.');
                })
                .catch(function () {
                    mostrarAviso('danger', 'Error de conexión al eliminar el valor. Revise su sesión e intente de nuevo.');
                })
                .then(function () {
                    $(btnConfirmar).prop('disabled', false);
                    pendienteEliminar = null;
                });
        });
    }

    // ─── Delegación (los botones de eliminar se crean dinámicamente) ─────────

    $(document).on('click', '.btn-guardar-fila', function () {
        guardarFila($(this).closest('tr'));
    });
});