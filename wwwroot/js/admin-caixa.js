// 1. MOTOR DO ATALHO
let modalAtalhosInstance = null;
document.addEventListener('keydown', function(e) {
    if (e.altKey && (e.key === 'm' || e.key === 'M')) {
        e.preventDefault();
        const modalElement = document.getElementById('modalAtalhos');
        if (!modalElement || modalElement.classList.contains('show')) return;
        if (!modalAtalhosInstance) modalAtalhosInstance = new bootstrap.Modal(modalElement);
        modalAtalhosInstance.show();
    }
});

// 2. ABRIR TELAS DE SANGRIA/SUPRIMENTO
function prepararSangria() { abrirModalOperacao('Sangria'); }
    function prepararSuprimento() { abrirModalOperacao('Suprimento'); }

    function abrirModalOperacao(tipo) {
        const mAtalhos = bootstrap.Modal.getInstance(document.getElementById('modalAtalhos'));
        if (mAtalhos) mAtalhos.hide();

        // Limpa rastro de valores antigos e coloca R$ fixo
        document.getElementById("valorOperacao").value = "R$ 0,00";
        document.getElementById("obsOperacao").value = "";

        document.getElementById("tipoOperacao").value = tipo;
        document.getElementById("tituloOperacao").innerText = tipo.toUpperCase();
        const header = document.getElementById("headerOperacao");
        header.className = tipo === 'Sangria' ? "modal-header bg-danger text-white" : "modal-header bg-success text-white";

        new bootstrap.Modal(document.getElementById('modalOperacao')).show();
    }

    async function salvarMovimentacao() {
        const tipo = document.getElementById("tipoOperacao").value;
        const valorRaw = document.getElementById("valorOperacao").value; // Ex: "R$ 100,00"
        const obs = document.getElementById("obsOperacao").value;

        // LIMPEZA SEGURA:
        // 1. Remove "R$" e espaços
        // 2. Removes o ponto (.) que é separador de milhar
        // 3. Troca a vírgula (,) por ponto (.) que é o separador decimal do C#
        let valorLimpo = valorRaw.replace("R$", "").trim();
        valorLimpo = valorLimpo.split('.').join(''); // Removes thousands separator
        valorLimpo = valorLimpo.replace(',', '.');   // Replaces decimal comma with point

        const valorNumerico = parseFloat(valorLimpo);

        if (isNaN(valorNumerico) || valorNumerico <= 0) {
            Swal.fire('Erro', 'Por favor, insira um valor válido!', 'error');
            return;
        }

        const data = new URLSearchParams();
        data.append('tipo', tipo);
        data.append('valor', valorNumerico.toFixed(2)); // Always sends format 100.00
        data.append('obs', obs);

        const resp = await fetch('/Caixa/RegistrarMovimentacao', { method: 'POST', body: data });
        const res = await resp.json();

        if (res.success) {
            Swal.fire('Sucesso!', 'Operação registrada.', 'success').then(() => location.reload());
        } else {
            // Se der erro, vamos ver o que o C# diz
            Swal.fire('Erro', res.message, 'error');
        }
    }

    async function verStatusCaixa() {
        try {
            const response = await fetch('/Caixa/ObterStatusAtual');
            const dados = await response.json();

            Swal.fire({
                title: '🏧 CONFERÊNCIA DE CAIXA',
                html: `
                    <div style="text-align: left;">
                        <p><b>(+) Suprimento (Abertura):</b> R$ ${dados.suprimento.toFixed(2)}</p>
                        <p><b>(+) Total Vendas:</b> R$ ${dados.vendas.toFixed(2)}</p>
                        <p><b>(-) Total Sangrias:</b> <span style="color:red">R$ ${dados.sangrias.toFixed(2)}</span></p>
                        <hr>
                        <h3 style="color:green"><b>SALDO EM GAVETA: R$ ${dados.saldo.toFixed(2)}</b></h3>
                    </div>
                `,
                icon: 'info'
            });
        } catch (e) {
            Swal.fire('Erro', 'Não foi possível consultar o saldo.', 'error');
        }
    }

    async function verRelatorioCaixa() {
        const modalAtalhos = bootstrap.Modal.getInstance(document.getElementById('modalAtalhos'));
        if (modalAtalhos) modalAtalhos.hide();

        try {
            const resp = await fetch('/Caixa/ObterRelatorioResumido');
            if (!resp.ok) throw new Error("Erro na rota");

            const dados = await resp.json();

            let tabelaHtml = `
                <div style="max-height: 300px; overflow-y: auto;">
                    <table class="table table-sm table-striped">
                        <thead>
                            <tr>
                                <th>Hora</th>
                                <th>Descrição</th>
                                <th class="text-end">Valor</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
            `;

            dados.movimentacoes.forEach(m => {
                // 1. Pega o tipo de forma segura (se vier nulo, vira texto vazio)
                const tipoTexto = m.tipo || "";
                const descricaoTexto = m.descricao || "";

                // 2. REGRA DE OURO: Se no Tipo ou na Descrição estiver escrito "Sangria",
                // ou se o valor for negativo, a gente pinta de vermelho.
                const ehSangria = tipoTexto.toLowerCase().includes('sangria') ||
                    descricaoTexto.toLowerCase().includes('sangria') ||
                    m.valor < 0;

                const cor = ehSangria ? 'text-danger' : 'text-success';
                const sinal = ehSangria ? '-' : '+';

                // 3. Formata o valor sempre positivo (o sinal a gente definiu acima)
                const valorDisplay = Math.abs(m.valor).toLocaleString('pt-BR', { minimumFractionDigits: 2 });

                tabelaHtml += `
            <tr>
                <td>${m.hora || '--:--'}</td>
                <td>${descricaoTexto || tipoTexto}</td>
                <td class="${cor} text-end"><strong>${sinal} R$ ${valorDisplay}</strong></td>
                <td class="text-center">
                    ${m.vendaId ? `<button onclick="reimprimirVenda(${m.vendaId})" class="btn btn-sm btn-primary">🖨️</button>` : ''}
                </td>
            </tr>`;
            });

            // --- AS LINHAS QUE FALTAVAM ESTÃO ABAIXO ---
            tabelaHtml += `</tbody></table></div>`;

            Swal.fire({
                title: '📊 RELATÓRIO DE MOVIMENTAÇÕES',
                html: tabelaHtml,
                width: '600px',
                confirmButtonText: 'FECHAR'
            });

        } catch (e) {
            Swal.fire('Erro', 'Falha ao carregar relatório: ' + e.message, 'error');
        }
    } // <--- FECHAMENTO FINAL DA FUNÇÃO

    function reimprimirVenda(vendaId) {
        if (!vendaId || vendaId === "null") {
            Swal.fire('Erro', 'ID de venda inválido.', 'error');
            return;
        }
        const url = `/Caixa/GerarCupom?id=${vendaId}`;
        window.open(url, "ImpressaoCupom", "width=400,height=600");
    }

    async function reimprimirUltimaVenda() {
        // Esconde o modal de atalhos antes de abrir o SweetAlert
        const modalAtalhos = bootstrap.Modal.getInstance(document.getElementById('modalAtalhos'));
        if (modalAtalhos) modalAtalhos.hide();

        const { value: opcao } = await Swal.fire({
            title: '🖨️ IMPRESSÃO',
            html: `
                <div style="text-align: left; margin-bottom: 10px;">
                    <b>1</b> - Última Venda<br>
                    <b>2</b> - Por ID da Venda<br>
                    <b>3</b> - Relatório do Dia (Resumo)
                </div>
                <input type="number" id="swal-input-opcao" class="form-control" placeholder="Digite 1, 2 ou 3" autofocus>
            `,
            showCancelButton: true,
            preConfirm: () => document.getElementById('swal-input-opcao').value
        });

        if (opcao === "1") {
            const resp = await fetch('/Caixa/ObterUltimaVendaId');
            const id = (await resp.text()).trim();
            if (id && id !== "") {
                reimprimirVenda(id);
            } else {
                Swal.fire('Aviso', 'Nenhuma venda encontrada hoje.', 'info');
            }
        } else if (opcao === "2") {
            const { value: idBusca } = await Swal.fire({ title: 'Digite o ID:', input: 'number', showCancelButton: true });
            if (idBusca) reimprimirVenda(idBusca);
        } else if (opcao === "3") {
            window.open('/Caixa/ImprimirRelatorioHoje', '_blank');
        }
    }

    // FUNÇÃO 1: O Menu que você abre pelo Atalho (Opções 1, 2 e 3)
    async function reimprimirMenu() {
        const modalAtalhos = bootstrap.Modal.getInstance(document.getElementById('modalAtalhos'));
        if (modalAtalhos) modalAtalhos.hide();

        const { value: opcao } = await Swal.fire({
            title: '🖨️ IMPRESSÃO',
            html: `
            <div style="text-align: left; margin-bottom: 10px;">
                <b>1</b> - Última Venda<br>
                <b>2</b> - Por ID da Venda<br>
                <b>3</b> - Relatório do Dia (Resumo)
            </div>
            <input type="number" id="swal-input-opcao" class="form-control" placeholder="Digite 1, 2 ou 3" autofocus>
        `,
            showCancelButton: true,
            preConfirm: () => document.getElementById('swal-input-opcao').value
        });

        if (opcao === "1") {
            const resp = await fetch('/Caixa/ObterUltimaVendaId');
            const id = (await resp.text()).trim(); // Adicione o .trim() para garantir que não vá espaço
            if (id) reimprimirVenda(id);
        } else if (opcao === "2") {
            const { value: idBusca } = await Swal.fire({ title: 'Digite o ID:', input: 'number', showCancelButton: true });
            reimprimirVenda(idBusca); // Chama a função técnica
        } else if (opcao === "3") {
            window.open('/Caixa/ImprimirRelatorioHoje', '_blank');
        }
    }


async function realizarFechamento() {
    const modalAtalhos = bootstrap.Modal.getInstance(document.getElementById('modalAtalhos'));
    if (modalAtalhos) modalAtalhos.hide();

    try {
        const resp = await fetch('/Caixa/ObterDadosFechamento');
        const d = await resp.json();

        // Tabela de Itens (Estoque)
        let estoqueHtml = d.produtosSaida.map(p => `
            <tr><td>${p.produto}</td><td class="text-end"><b>${p.quantidade.toFixed(3).replace('.', ',')} KG</b></td></tr>
        `).join('');

        // Histórico de Vendas
        let historicoHtml = d.historicoVendas.map(v => `
            <tr><td>${v.hora}</td><td>${v.itens}</td><td class="text-end">R$ ${v.total.toFixed(2)}</td></tr>
        `).join('');

        Swal.fire({
            title: '🔍 AUDITORIA E FECHAMENTO',
            html: `
                <div class="text-start" style="font-size: 13px;">
                    <h6 class="fw-bold">1. Saídas de Estoque</h6>
                    <div style="max-height: 120px; overflow-y: auto;" class="border rounded mb-2">
                        <table class="table table-sm mb-0"><tbody>${estoqueHtml}</tbody></table>
                    </div>

                    <h6 class="fw-bold">2. Histórico de Vendas</h6>
                    <div style="max-height: 150px; overflow-y: auto;" class="border rounded mb-2">
                        <table class="table table-sm mb-0"><tbody>${historicoHtml}</tbody></table>
                    </div>

                    <div class="p-2 bg-light border rounded">
                        <div class="d-flex justify-content-between text-primary"><span>(+) Vendas:</span> <span>R$ ${d.totalVendas.toFixed(2)}</span></div>
                        <div class="d-flex justify-content-between text-success"><span>(+) Suprimentos:</span> <span>R$ ${d.suprimentos.toFixed(2)}</span></div>
                        <div class="d-flex justify-content-between text-danger"><span>(-) Sangrias:</span> <span>R$ ${d.sangrias.toFixed(2)}</span></div>
                        <hr class="my-1">
                        <div class="d-flex justify-content-between fw-bold text-dark fs-5">
                            <span>TOTAL EM GAVETA:</span> <span>R$ ${d.saldoFinal.toFixed(2)}</span>
                        </div>
                    </div>
                </div>
            `,
            width: '650px',
            showCancelButton: true,
            confirmButtonText: '✅ TUDO OK, ZERAR DIA',
            confirmButtonColor: '#198754'
        }).then((result) => {
            if (result.isConfirmed) {
                confirmarZeramentoFinal();
            }
        });
    } catch (e) {
        Swal.fire('Erro', 'Erro ao carregar dados.', 'error');
    }
}

async function confirmarZeramentoFinal() {
    const res = await fetch('/Caixa/ZerarCaixaParaProximoDia', { method: 'POST' });
    const data = await res.json();

    if (data.success) {
        Swal.fire('Sucesso', 'Caixa encerrado e zerado!', 'success').then(() => location.reload());
    } else {
        Swal.fire('Erro', 'Não foi possível zerar: ' + data.message, 'error');
    }
}

    // Substitua sua função formatarMoedaParaSweet por esta:
    function formatarMoedaParaSweet(input) {
        let valor = input.value.replace(/\D/g, "");
        if (valor === "" || valor === "0") {
            input.value = "R$ 0,00";
            return;
        }
        let resultado = (parseInt(valor) / 100).toFixed(2).replace(".", ",");
        input.value = "R$ " + resultado;
} 

// --- ESTA É A FUNÇÃO QUE ESTAVA FALTANDO ---
async function buscarProdutoPorCodigo(codigo) {
    if (!codigo) return;
    try {
        const resp = await fetch(`/Caixa/BuscarPorCodigo?codigo=${codigo}`);
        const data = await resp.json();

        console.log("DADOS QUE CHEGARAM DO C#:", data);

        if (data.success) {
            // Preenche itemAtual com os dados reais (inclui estoque)
            itemAtual = {
                id: data.id,
                nome: data.nome,
                preco: parseFloat(data.preco),
                regra: data.regra,
                estoque: data.estoque ?? 0
            };

            // Atualiza a UI principal (sem abrir modal)
            const displayNome = document.getElementById("displayNome");
            const exibirPreco = document.getElementById("exibirPreco");
            const campoQtd = document.getElementById("modalCampoQtd");

            if (displayNome) displayNome.innerText = itemAtual.nome;
            if (exibirPreco) exibirPreco.innerText = "R$ " + (itemAtual.preco || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2 });
            if (campoQtd) {
                // Se for UNIDADE, já coloca 1; se for GRANEL, limpa para o operador informar
                campoQtd.value = (String(itemAtual.regra || "").toUpperCase() === "UNIDADE") ? "1" : "";
                campoQtd.focus();
                campoQtd.select();
            }

            // Guarda o estoque visível (opcional)
            const modalEstoque = document.getElementById("modalEstoqueDisponivel");
            if (modalEstoque) modalEstoque.innerText = itemAtual.estoque;

            // Limpa o campo de busca (visual)
            const campoBusca = document.getElementById("campoBusca");
            if (campoBusca) campoBusca.value = "";

        } else {
            Swal.fire('Ops!', 'Produto não encontrado.', 'warning');
            const campoBusca = document.getElementById("campoBusca");
            if (campoBusca) campoBusca.value = "";
        }
    } catch (e) {
        console.error("Erro na busca:", e);
    }
}

document.addEventListener("DOMContentLoaded", function () {
    // 1. Buscamos o campo usando o ID padronizado 'campoBusca'
    const inputBusca = document.getElementById("campoBusca");

    if (inputBusca) {
        // Foca no campo automaticamente ao abrir a página
        inputBusca.focus();

        // 2. Adicionamos o "escutador" para a tecla Enter
        inputBusca.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault(); // Evita que a página atualize
                console.log("Enter detectado. Valor digitado:", this.value);

                // Chama a função de busca enviando o que foi digitado
                buscarProdutoPorCodigo(this.value);
            }
        });
        console.log("Sistema pronto: Campo 'campoBusca' monitorado.");
    } else {
        // Se cair aqui, é porque o ID no HTML ainda está diferente de 'campoBusca'
        console.error("ERRO: O elemento 'campoBusca' não foi encontrado no HTML.");
    }
});

        document.addEventListener('DOMContentLoaded', function () {
    const inputBusca = document.getElementById("campoCodigo");

    if (inputBusca) {
        inputBusca.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault(); // Impede o formulário de recarregar a página
                console.log("Enter detectado no campo busca. Valor:", this.value);
                buscarProdutoPorCodigo(this.value);
            }
        });
    }
});

document.addEventListener("DOMContentLoaded", function () {
    const inputBusca = document.getElementById("campoBusca");
    const sugestoesDiv = document.getElementById("sugestoes");
    const dadosProdutos = document.querySelectorAll("#dadosProdutos div");

    if (!inputBusca || !sugestoesDiv) return;

    inputBusca.focus();

    inputBusca.addEventListener("keydown", function (event) {
        if (event.key === "Enter") {
            event.preventDefault();
            buscarProdutoPorCodigo(this.value);
        }
    });

    inputBusca.addEventListener("input", function () {
        const termo = this.value.toLowerCase().trim().normalize("NFD").replace(/[\u0300-\u036f]/g, "");
        sugestoesDiv.innerHTML = "";

        if (!termo) {
            sugestoesDiv.style.display = "none";
            return;
        }

        let encontrados = 0;
        dadosProdutos.forEach(div => {
            const nomeOriginal = (div.dataset.nome || "").toString();
            const nomeSemAcento = nomeOriginal.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "");

            if (nomeSemAcento.startsWith(termo) && encontrados < 8) {
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = "list-group-item list-group-item-action py-2";
                btn.innerHTML = `<strong>${nomeOriginal}</strong> <small class="text-muted float-end">R$ ${div.dataset.preco}</small>`;

                btn.onclick = () => {
                    // Busca dados reais (inclui estoque) e preenche o campo quantidade
                    buscarProdutoPorCodigo(div.dataset.id);
                    sugestoesDiv.style.display = "none";
                    inputBusca.value = div.dataset.nome;
                };

                sugestoesDiv.appendChild(btn);
                encontrados++;
            }
        });

        sugestoesDiv.style.display = encontrados > 0 ? "block" : "none";
    });
});

// Adicione isto ao final de wwwroot/js/admin-caixa.js (antes do fechamento do arquivo)
// Garante que a função exista no escopo global e renderize o carrinho usando a variável `carrinho`.

function renderCarrinho() {
    const tbody = document.getElementById("carrinho");
    if (!tbody) return;
    tbody.innerHTML = "";
    let totalGeral = 0;

    carrinho.forEach((item, index) => {
        const preco = parseFloat(item.preco || 0);
        const qtd = parseFloat(item.qtd ?? item.Quantidade ?? item.quantidade ?? 0) || 0;
        const subtotal = parseFloat(item.subtotal ?? (preco * qtd)) || 0;
        totalGeral += subtotal;

        const labelQtd = item.labelQtd ?? (item.qtd ?? item.Quantidade ?? item.quantidade) ?? qtd;
        const precoDisplay = preco.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const subtotalDisplay = subtotal.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

        tbody.insertAdjacentHTML('beforeend', `
            <tr>
                <td>${item.nome || 'Sem nome'}</td>
                <td>${labelQtd}</td>
                <td>R$ ${precoDisplay}</td>
                <td class="fw-bold">R$ ${subtotalDisplay}</td>
                <td>
                    <button onclick="removerItem(${index})" class="btn btn-sm btn-danger">x</button>
                </td>
            </tr>
        `);
    });

    const displayTotal = document.getElementById("displayTotal");
    if (displayTotal) {
        displayTotal.innerText = totalGeral.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    }
}

// Se não existir, definimos uma função removerItem global para manter consistência
if (typeof removerItem === 'undefined') {
    function removerItem(i) {
        carrinho.splice(i, 1);
        renderCarrinho();
    }
}

// Substitua a função que abre o modal de pagamento para garantir que backdrops do Bootstrap não bloqueiem os botões
function abrirModalPagamento() {
    if (carrinho.length === 0) {
        Swal.fire('Aviso', 'O carrinho está vazio!', 'info');
        return;
    }

    // Validação de estoque (mantida)
    let errosEstoque = [];
    carrinho.forEach(item => {
        const qtdPedida = parseFloat(String(item.qtd ?? item.Quantidade ?? item.quantidade ?? 0).replace(',', '.')) || 0;
        const estoqueReal = parseFloat(String(item.estoque ?? 0).replace(',', '.')) || 0;
        if (qtdPedida > estoqueReal) {
            let unidade = (String(item.tipo || '').toUpperCase() === "PESO" || String(item.tipo || '').toUpperCase() === "GRANEL") ? "kg" : "un";
            errosEstoque.push(`Produto: <b>${item.nome}</b><br>Solicitado: ${qtdPedida}${unidade} | Disponível: ${estoqueReal}${unidade}`);
        }
    });

    if (errosEstoque.length > 0) {
        Swal.fire({
            title: 'Indisponibilidade de Estoque',
            html: `<div style="text-align: center;">${errosEstoque.join('<br><hr>')}</div>`,
            icon: 'warning',
            confirmButtonColor: '#d33'
        });
        return;
    }

    // Remove backdrops restantes do Bootstrap e estado modal
    document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
    document.body.classList.remove('modal-open');
    document.body.style.overflow = '';

    // Mostra o modal custom com z-index alto para garantir interatividade
    const modal = document.getElementById("modalPagamento");
    if (modal) {
        modal.style.display = "flex";
        modal.style.zIndex = "11000"; // acima de eventuais overlays (ex.: SweetAlert)
        const box = modal.querySelector('.modal-box');
        if (box) {
            box.style.zIndex = "11001";
            box.style.pointerEvents = "auto";
        }
        // foco no primeiro botão de método para acessibilidade
        setTimeout(() => {
            const primeiro = modal.querySelector('.btn-metodo-sel');
            if (primeiro) primeiro.focus();
        }, 60);
    }
}

function fecharModal() {
    const modal = document.getElementById("modalPagamento");
    if (modal) {
        modal.style.display = "none";
        modal.style.zIndex = "";
        const box = modal.querySelector('.modal-box');
        if (box) {
            box.style.zIndex = "";
            box.style.pointerEvents = "";
        }
    }
    document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
    document.body.classList.remove('modal-open');
    document.body.style.overflow = '';
}

// Função adicionada para selecionar método de pagamento (preserve padrão visual que você tinha)
function marcarMetodo(btn, metodo) {
    // Restaura estilo padrão em todos
    document.querySelectorAll('.btn-metodo-sel').forEach(b => {
        b.style.background = "#f8f9fa";
        b.style.color = "#004d1a";
    });

    // Destaca o selecionado
    if (btn) {
        btn.style.background = "#004d1a";
        btn.style.color = "white";
    }

    // Atualiza variável global e campo oculto (se presente)
    metodoSelecionado = metodo;
    const inputMetodo = document.getElementById("inputMetodoPagamento");
    if (inputMetodo) inputMetodo.value = metodo;

    // Mostra/oculta section de troco quando for Dinheiro
    const divTroco = document.getElementById("secaoTroco");
    if (divTroco) {
        if (String(metodo).toLowerCase().includes('dinheiro')) {
            divTroco.style.display = "block";
            setTimeout(() => document.getElementById("valorRecebidoInput")?.focus(), 100);
        } else {
            divTroco.style.display = "none";
            const valRec = document.getElementById("valorRecebidoInput");
            const displayTroco = document.getElementById("displayTroco");
            if (valRec) valRec.value = "";
            if (displayTroco) displayTroco.innerText = "R$ 0,00";
            const hiddenTroco = document.getElementById("valorTrocoInput");
            if (hiddenTroco) hiddenTroco.value = "0";
        }
    }
}

// Abre a etapa do CPF / valida cupom vendedor
async function irParaCPF() {
    if (!metodoSelecionado || metodoSelecionado === "") {
        Swal.fire('Atenção', 'Escolha o método de pagamento!', 'warning');
        return;
    }

    // Fecha temporariamente o modal custom para exibir o SweetAlert do cupom
    fecharModal();

    // Pede o código do vendedor via SweetAlert
    const { value: cupom } = await Swal.fire({
        title: 'CÓDIGO DO VENDEDOR',
        input: 'text',
        inputLabel: 'Identifique o vendedor para continuar',
        inputPlaceholder: 'Digite o código (ex: LEO)',
        showCancelButton: true,
        confirmButtonText: 'Validar',
        cancelButtonText: 'Cancelar',
        allowOutsideClick: false,
        inputValidator: (value) => {
            if (!value) return 'O cupom é obrigatório!';
        }
    });

    if (!cupom) {
        // Cancelou: reabre a tela de seleção de método
        abrirModalPagamento();
        return;
    }

    try {
        const resp = await fetch(`/Caixa/ValidarCupom?codigo=${encodeURIComponent(cupom)}`);
        const data = await resp.json();

        if (data.exists) {
            codigoVendedorInformado = cupom;

            // Mostra a seção CPF dentro do mesmo modal custom
            const modal = document.getElementById("modalPagamento");
            if (modal) {
                // garante que o modal esteja visível e em foco
                modal.style.display = "flex";
                modal.style.zIndex = "11000";
            }

            // Esconde a seção de pagamento e mostra a do CPF
            const secaoPagamento = document.getElementById("secaoPagamento");
            const secaoCPF = document.getElementById("secaoCPF");
            if (secaoPagamento) secaoPagamento.style.display = "none";
            if (secaoCPF) secaoCPF.style.display = "block";

            // Foca no campo de CPF
            setTimeout(() => document.getElementById("cpfClienteInput")?.focus(), 100);
        } else {
            await Swal.fire('Erro', 'Cupom não encontrado!', 'error');
            // Reabre e pede novamente
            irParaCPF();
        }
    } catch (e) {
        console.error("Erro validando cupom:", e);
        Swal.fire('Erro', 'Falha ao validar cupom. Tente novamente.', 'error').then(() => abrirModalPagamento());
    }
}

// Volta para a seção de seleção de método dentro do modal de pagamento
function voltarParaPagamento() {
    const secaoPagamento = document.getElementById("secaoPagamento");
    const secaoCPF = document.getElementById("secaoCPF");
    if (secaoPagamento) secaoPagamento.style.display = "block";
    if (secaoCPF) secaoCPF.style.display = "none";

    // Reabre o modal custom (caso esteja fechado)
    const modal = document.getElementById("modalPagamento");
    if (modal) {
        modal.style.display = "flex";
        modal.style.zIndex = "11000";
    }
}

// 5) enviarVendaDeVez - normaliza shape e formatação antes de enviar
async function enviarVendaDeVez() {
    if (carrinho.length === 0) {
        Swal.fire('Carrinho Vazio', 'Adicione produtos antes de finalizar.', 'warning');
        return;
    }

    const metodo = metodoSelecionado;
    const cpf = document.getElementById("cpfClienteInput")?.value || "";
    const vendedor = typeof codigoVendedorInformado !== 'undefined' ? codigoVendedorInformado : "";
    const valorRec = document.getElementById("valorRecebidoInput")?.value || "0";
    const valorTro = document.getElementById("valorTrocoInput")?.value || "0";

    const data = new URLSearchParams();
    data.append("metodoPagamento", metodo);
    data.append("cpfCliente", cpf);
    data.append("codigoVendedor", vendedor);
    data.append("valorRecebido", String(valorRec).replace(',', '.'));
    data.append("valorTroco", String(valorTro).replace(',', '.'));

    carrinho.forEach((item, i) => {
        const prefixo = `itens[${i}]`;

        const produtoId = item.ProdutoId ?? item.id ?? "";
        data.append(`${prefixo}.ProdutoId`, produtoId.toString());

        let qtdRaw = item.Quantidade ?? item.qtd ?? item.quantidade ?? "0";
        qtdRaw = (typeof qtdRaw === "number") ? qtdRaw.toString() : String(qtdRaw);
        qtdRaw = qtdRaw.replace(',', '.');

        let precoRaw = item.Preco ?? item.preco ?? "0";
        precoRaw = (typeof precoRaw === "number") ? precoRaw.toString() : String(precoRaw);
        precoRaw = precoRaw.replace(',', '.');

        data.append(`${prefixo}.Quantidade`, qtdRaw);
        data.append(`${prefixo}.Preco`, precoRaw);
    });

    try {
        Swal.showLoading();

        const response = await fetch('/Caixa/FinalizarVenda', {
            method: 'POST',
            body: data,
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded'
            }
        });

        const res = await response.json();

        if (res.success) {
            // Fecha nosso modal custom e garante que não existam backdrops bloqueando
            try { fecharModal(); } catch (e) { /* noop se não existir */ }
            document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
            document.body.classList.remove('modal-open');
            document.body.style.overflow = '';

            // Abre SweetAlert garantindo z-index acima do modal
            Swal.fire({
                title: 'Venda Finalizada!',
                text: 'Sucesso ao processar a venda.',
                icon: 'success',
                confirmButtonText: 'OK',
                allowOutsideClick: false,
                didOpen: () => {
                    const c = document.querySelector('.swal2-container');
                    if (c) c.style.zIndex = '12000';
                }
            }).then(() => {
                if (res.vendaId) window.open('/Caixa/GerarCupom?id=' + res.vendaId, '_blank');
                location.reload();
            });
        } else {
            // Em caso de erro, também fecha o modal custom antes de mostrar o erro
            try { fecharModal(); } catch (e) { /* noop */ }
            document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
            document.body.classList.remove('modal-open');
            document.body.style.overflow = '';

            Swal.fire({
                title: 'Erro no Estoque',
                html: res.message.replace(/\n/g, '<br>'),
                icon: 'error',
                confirmButtonColor: '#d33',
                didOpen: () => {
                    const c = document.querySelector('.swal2-container');
                    if (c) c.style.zIndex = '12000';
                }
            });
        }
    } catch (e) {
        console.error("Erro crítico no envio:", e);
        try { fecharModal(); } catch (ex) { /* noop */ }
        document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
        document.body.classList.remove('modal-open');
        document.body.style.overflow = '';
        Swal.fire('Erro de Comunicação', 'O servidor não respondeu. Verifique sua conexão.', 'error');
    }
}

// Calcula o troco a partir do carrinho e do valor recebido pelo operador
function calcularTroco() {
    const valorRecebidoEl = document.getElementById("valorRecebidoInput");
    const displayTroco = document.getElementById("displayTroco");
    const valorTrocoInput = document.getElementById("valorTrocoInput");

    if (!displayTroco || !valorTrocoInput) return;

    // Pega valor recebido (normaliza vírgula/point)
    let valorRecebidoRaw = (valorRecebidoEl?.value ?? "0").toString().replace(',', '.').trim();
    let valorRecebido = parseFloat(valorRecebidoRaw);
    if (isNaN(valorRecebido)) valorRecebido = 0;

    // Calcula total do carrinho (seguro contra formatos mistos)
    let total = 0;
    (carrinho || []).forEach(item => {
        const preco = parseFloat(String(item.preco ?? item.Preco ?? 0).replace(',', '.')) || 0;
        const qtd = parseFloat(String(item.qtd ?? item.Quantidade ?? item.quantidade ?? 0).replace(',', '.')) || 0;
        total += preco * qtd;
    });

    // Troco lógico
    const diff = (valorRecebido - total);
    if (diff >= 0.0005) {
        // Tem troco para devolver
        const troco = diff;
        displayTroco.innerText = "R$ " + troco.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        displayTroco.classList.remove('text-danger');
        displayTroco.classList.add('text-success');
        valorTrocoInput.value = troco.toFixed(2).replace(',', '.');
    } else {
        // Valor insuficiente ou igual -> mostra quanto falta em vermelho
        const falta = Math.abs(diff);
        if (falta < 0.0005) {
            displayTroco.innerText = "R$ 0,00";
        } else {
            displayTroco.innerText = "Faltam R$ " + falta.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }
        displayTroco.classList.remove('text-success');
        displayTroco.classList.add('text-danger');
        valorTrocoInput.value = "0";
    }
}