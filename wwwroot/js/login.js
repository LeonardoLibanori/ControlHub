(function () {
    var campoSenha = document.getElementById('senha');
    var btnMostrar = document.getElementById('btnMostrarSenha');
    var avisoCaps = document.getElementById('avisoCaps');
    var btnEntrar = document.getElementById('btnEntrar');
    var form = document.getElementById('formLogin');

    if (!campoSenha || !btnMostrar || !btnEntrar || !form) return;

    // Mostrar / Ocultar senha
    btnMostrar.addEventListener('click', function () {
        var mostrar = campoSenha.type === 'password';
        campoSenha.type = mostrar ? 'text' : 'password';
        btnMostrar.setAttribute('aria-pressed', mostrar ? 'true' : 'false');
        btnMostrar.textContent = mostrar ? 'Ocultar' : 'Mostrar';
        campoSenha.focus();
    });

    // Detectar Caps Lock
    function atualizarCaps(event) {
        if (event.getModifierState && event.getModifierState('CapsLock')) {
            avisoCaps.style.display = 'inline';
        } else {
            avisoCaps.style.display = 'none';
        }
    }
    campoSenha.addEventListener('keydown', atualizarCaps);
    campoSenha.addEventListener('keyup', atualizarCaps);

    // Travar botão no submit: uma só requisição
    form.addEventListener('submit', function () {
        btnEntrar.disabled = true;
        btnEntrar.textContent = 'Entrando…';
    });
})();