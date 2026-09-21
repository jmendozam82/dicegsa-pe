// PE-GOL SaaS — JS global (HU-045 Tanda A.2)
// Toggle del sidebar en resoluciones < 768px (drawer overlay, DS § 11).
$(function () {
    'use strict';

    var toggle = document.getElementById('sidebarToggle');
    if (!toggle) {
        return; // Layout sin navbar (p. ej. login).
    }

    toggle.addEventListener('click', function () {
        var abierto = document.body.classList.toggle('sidebar-open');
        toggle.setAttribute('aria-expanded', abierto ? 'true' : 'false');
    });

    // Cierra el drawer al hacer clic en el backdrop.
    document.body.addEventListener('click', function (event) {
        if (document.body.classList.contains('sidebar-open') &&
            event.target === document.body) {
            document.body.classList.remove('sidebar-open');
            toggle.setAttribute('aria-expanded', 'false');
        }
    });
});