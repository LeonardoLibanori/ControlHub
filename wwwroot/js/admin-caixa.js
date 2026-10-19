// BUG-05 (seguranca): os POSTs exigem token anti-forgery ([ValidateAntiForgeryToken]).
// Os <form> normais ja enviam o token em um input hidden gerado pelo Razor, mas
// os POSTs feitos por fetch() precisam mandar o token no header X-CSRF-TOKEN
// (configurado em Program.cs). O token vem do <meta name="csrf-token"> do _Layout.
// BUG-13 (autorizacao): as chamadas do painel se identificam como AJAX.
// Quando falta permissao (ex.: Operador tentando sangria), o servidor responde
// 403 direto em vez de redirecionar para a pagina HTML de acesso negado -
// sem isso o fetch seguia o 302, recebia 200 com HTML e o resp.json() quebrava,
// escondendo o motivo real do bloqueio do operador.
function csrfHeaders(extra) {
    const meta = document.querySelector('meta[name="csrf-token"]');
    const token = meta ? meta.getAttribute('content') : null;
    return Object.assign({}, extra || {}, {
        'X-CSRF-TOKEN': token || '',
        'X-Requested-With': 'XMLHttpRequest'
    });
}


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
        data.append('valor', valorNumerico.toFixed(2)); // Always sends format 100.00
        data.append('obs', obs);

        // BUG-13: a rota depende do TIPO, e o tipo agora mora na rota.
        // RegistrarSangria e exclusiva do Dono: um Operador que tentar leva 403
        // (o menu esconde o botao, mas a API tambem barra - defesa em camadas).
        const rota = (tipo === 'Sangria')
            ? '/Caixa/RegistrarSangria'
            : '/Caixa/RegistrarSuprimento';

        const resp = await fetch(rota, { method: 'POST', body: data, headers: csrfHeaders() });

        // 403 = autenticado mas sem permissao (falta o papel de Dono).
        if (resp.status === 403) {
            Swal.fire('Acesso negado', 'Apenas o Dono pode registrar sangria.', 'error');
            return;
        }

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
                title: 'Conferência de caixa',
                html: `
                    <div style="text-align: left;">
                        <p><b>(+) Suprimento (Abertura):</b> R$ ${dados.suprimento.toFixed(2)}</p>
                        <p><b>(+) Total Vendas:</b> R$ ${dados.vendas.toFixed(2)}</p>
                        <p><b>(-) Total Sangrias:</b> <span class="cash-negative">R$ ${dados.sangrias.toFixed(2)}</span></p>
                        <hr>
                        <h3 class="cash-positive"><b>Saldo em gaveta: R$ ${dados.saldo.toFixed(2)}</b></h3>
                    </div>
                `,
                customClass: {
                    popup: 'swal-info'
                }
            });
        } catch (e) {
            Swal.fire({
                title: 'Erro',
                text: 'Não foi possível consultar o saldo.',
                customClass: {
                    popup: 'swal-error'
                }
            });
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
                    ${m.vendaId ? `<button onclick="reimprimirVenda(${m.vendaId})" class="btn btn-sm btn-primary" title="Reimprimir cupom" aria-label="Reimprimir cupom"><i class="bi bi-printer" aria-hidden="true"></i></button>` : ''}
                </td>
            </tr>`;
            });

            // --- AS LINHAS QUE FALTAVAM ESTÃO ABAIXO ---
            tabelaHtml += `</tbody></table></div>`;

            Swal.fire({
                title: 'Relatório de movimentações',
                customClass: {
                    popup: 'swal-info'
                },
                html: tabelaHtml,
                width: '600px',
                confirmButtonText: 'Fechar'
            });

        } catch (e) {
            Swal.fire({
                title: 'Erro',
                text: 'Falha ao carregar relatório: ' + e.message,
                customClass: {
                    popup: 'swal-error'
                }
            });
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
            title: 'Impressão',
            customClass: {
                popup: 'swal-info'
            },
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
                Swal.fire({
                    title: 'Aviso',
                    text: 'Nenhuma venda encontrada hoje.',
                    customClass: {
                        popup: 'swal-info'
                    }
                });
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
            title: 'Impressão',
            customClass: {
                popup: 'swal-info'
            },
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
            title: 'Auditoria e fechamento',
            customClass: {
                popup: 'swal-info'
            },
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
                            <span>Total em gaveta:</span> <span>R$ ${d.saldoFinal.toFixed(2)}</span>
                        </div>
                    </div>
                </div>
            `,
            width: '650px',
            showCancelButton: true,
            confirmButtonText: 'Tudo ok, zerar dia',
            confirmButtonColor: '#c2a36d'
        }).then((result) => {
            if (result.isConfirmed) {
                confirmarZeramentoFinal();
            }
        });
    } catch (e) {
        Swal.fire({
            title: 'Erro',
            text: 'Erro ao carregar dados.',
            customClass: {
                popup: 'swal-error'
            }
        });
    }
}

async function confirmarZeramentoFinal() {
    const res = await fetch('/Caixa/ZerarCaixaParaProximoDia', { method: 'POST', headers: csrfHeaders() });
    const data = await res.json();

    if (data.success) {
        Swal.fire({
            title: 'Sucesso',
            text: 'Caixa encerrado e zerado!',
            customClass: {
                popup: 'swal-success'
            }
        }).then(() => location.reload());
    } else {
        Swal.fire({
            title: 'Erro',
            text: 'Não foi possível zerar: ' + data.message,
            customClass: {
                popup: 'swal-error'
            }
        });
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
                unidade: data.unidade || (String(data.regra || '').toUpperCase() === 'PORPESO' ? 'kg' : 'un'),
                estoque: data.estoque ?? 0
            };

            // Atualiza a UI principal (sem abrir modal)
            const displayNome = document.getElementById("displayNome");
            const exibirPreco = document.getElementById("exibirPreco");
            const campoQtd = document.getElementById("modalCampoQtd");
            const labelQtd = document.getElementById("labelQtd");
            const campoQuantidade = document.getElementById("campoQtd");

            if (displayNome) displayNome.innerText = itemAtual.nome;
            if (exibirPreco) exibirPreco.innerText = "R$ " + (itemAtual.preco || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2 }) + ` /${itemAtual.unidade}`;
            if (labelQtd) labelQtd.innerText = itemAtual.unidade === "kg" ? "QUANTIDADE (kg)" : "QUANTIDADE (un)";
            if (campoQuantidade) campoQuantidade.step = itemAtual.unidade === "kg" ? "0.001" : "1";
            if (campoQtd) {
                campoQtd.value = itemAtual.unidade === "kg" ? "" : "1";
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
        // Sem 'campoBusca' NAO e erro: este arquivo (admin-caixa.js) e
        // carregado pelo _Layout em TODAS as telas, e so o PDV tem o campo de
        // busca por codigo de barras. Antes, qualquer tela fora do PDV
        // (Historico, Dashboard, Estoque) imprimia um ERRO VERMELHO no
        // console, dando a impressao de que a pagina estava quebrada.
        console.debug("admin-caixa: sem 'campoBusca' nesta tela (normal fora do PDV).");
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
                // BUG-23 (XSS armazenado): o nome vinha cru por innerHTML e um
                // produto chamado "<img src=x onerror=alert(1)>" executava script
                // no PDV. Montamos os nos via DOM (textContent escapa o texto).
                const nomeEl = document.createElement("strong");
                nomeEl.textContent = nomeOriginal;
                const precoEl = document.createElement("small");
                precoEl.className = "text-muted float-end";
                precoEl.textContent = `R$ ${div.dataset.preco} /${div.dataset.unidade || 'un'}`;
                btn.appendChild(nomeEl);
                btn.appendChild(document.createTextNode(" "));
                btn.appendChild(precoEl);

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

        // BUG-23 (XSS armazenado): o nome do produto era interpolado em
        // insertAdjacentHTML, executando qualquer tag HTML salva no cadastro.
        // A linha e montada com createElement/textContent (texto sempre escapado).
        const tr = document.createElement("tr");

        const tdNome = document.createElement("td");
        tdNome.textContent = item.nome || 'Sem nome';

        const tdQtd = document.createElement("td");
        tdQtd.textContent = String(labelQtd);

        const tdPreco = document.createElement("td");
        tdPreco.textContent = `R$ ${precoDisplay} /${item.unidade || 'un'}`;

        const tdSubtotal = document.createElement("td");
        tdSubtotal.className = "fw-bold";
        tdSubtotal.textContent = `R$ ${subtotalDisplay}`;

        const tdAcoes = document.createElement("td");
        const btnRemover = document.createElement("button");
        btnRemover.type = "button";
        btnRemover.className = "btn btn-sm btn-danger";
        btnRemover.textContent = "x";
        btnRemover.addEventListener("click", () => removerItem(index));
        tdAcoes.appendChild(btnRemover);

        tr.appendChild(tdNome);
        tr.appendChild(tdQtd);
        tr.appendChild(tdPreco);
        tr.appendChild(tdSubtotal);
        tr.appendChild(tdAcoes);
        tbody.appendChild(tr);
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
        Swal.fire({
            title: 'Aviso',
            text: 'O carrinho está vazio!',
            customClass: {
                popup: 'swal-info'
            }
        });
        return;
    }

    // Validação de estoque (mantida)
    let errosEstoque = [];
    carrinho.forEach(item => {
        const qtdPedida = parseFloat(String(item.qtd ?? item.Quantidade ?? item.quantidade ?? 0).replace(',', '.')) || 0;
        const estoqueReal = parseFloat(String(item.estoque ?? 0).replace(',', '.')) || 0;
        if (qtdPedida > estoqueReal) {
            let unidade = item.unidade || (String(item.regra || item.tipo || '').toUpperCase() === "PORPESO" ? "kg" : "un");
            errosEstoque.push(`Produto: <b>${item.nome}</b><br>Solicitado: ${qtdPedida}${unidade} | Disponível: ${estoqueReal}${unidade}`);
        }
    });

    if (errosEstoque.length > 0) {
        Swal.fire({
            title: 'Indisponibilidade de Estoque',
            html: `<div style="text-align: center;">${errosEstoque.join('<br><hr>')}</div>`,
            customClass: {
                popup: 'swal-warning'
            },
            confirmButtonColor: '#d33'
        });
        return;
    }

    // Remove backdrops restantes do Bootstrap e estado modal
    document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
    document.body.classList.remove('modal-open');
    document.body.style.overflow = '';

    // BUG-30: reabrir o modal reinicia o estado - metodo, valor recebido, troco
    // e aviso. Sem isto, o valor da venda anterior ficaria no campo e o
    // CONFIRMAR abriria habilitado para um valor que nao e o desta venda.
    resetarEtapaPagamento();

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
    document.querySelectorAll('.btn-metodo-sel').forEach(button => button.classList.remove('is-selected'));
    btn?.classList.add('is-selected');

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
            if (valRec) valRec.value = "";
            const hiddenTroco = document.getElementById("valorTrocoInput");
            if (hiddenTroco) hiddenTroco.value = "0";
        }
    }

    // BUG-30: trocar de metodo recalcula o CONFIRMAR. Sair de Dinheiro com um
    // valor insuficiente volta a liberar o botao (Pix/Debito/Credito nao usam
    // valor recebido); entrar em Dinheiro com o campo vazio o bloqueia.
    calcularTroco();
}

// Abre a etapa do CPF. O cupom do vendedor NAO e mais obrigatorio: virou um
// botao opcional dentro da propria etapa (ver gerenciarCupomVendedor), para
// lojas que usam programa de recompensa por venda.
function irParaCPF() {
    if (!metodoSelecionado || metodoSelecionado === "") {
        Swal.fire('Atenção', 'Escolha o método de pagamento!', 'warning');
        return;
    }

    // BUG-30: defesa em profundidade. O botao ja vem desabilitado por
    // podeConfirmarPagamento(), mas o onclick pode ser chamado por fora (Enter,
    // automacao, devtools). Aqui reconferimos antes de avancar: se faltar
    // dinheiro, o operador recebe o aviso e o modal NAO avanca.
    if (!podeConfirmarPagamento()) {
        const total = formatarCentavos(totalCarrinhoEmCentavos());
        Swal.fire(
            'Valor insuficiente',
            `O valor recebido em dinheiro precisa cobrir o total de R$ ${total}.`,
            'warning'
        );
        document.getElementById("valorRecebidoInput")?.focus();
        return;
    }

    // Esconde a seção de pagamento e mostra a do CPF (sem fechar o modal)
    const secaoPagamento = document.getElementById("secaoPagamento");
    const secaoCPF = document.getElementById("secaoCPF");
    if (secaoPagamento) secaoPagamento.style.display = "none";
    if (secaoCPF) secaoCPF.style.display = "block";

    // Foca no campo de CPF
    setTimeout(() => document.getElementById("cpfClienteInput")?.focus(), 100);
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

// CUPOM DO VENDEDOR (opcional): botao na etapa do CPF para adicionar, trocar
// ou remover o cupom. A venda so e validada quando o operador decide informar.
function atualizarBotaoCupomVendedor() {
    const btn = document.getElementById("btnCupomVendedor");
    const texto = document.getElementById("btnCupomVendedorTexto");
    if (!btn || !texto) return;

    if (codigoVendedorInformado) {
        texto.textContent = `VENDEDOR: ${codigoVendedorInformado} ✓`;
        btn.classList.remove("btn-outline-secondary");
        btn.classList.add("btn-outline-success");
    } else {
        texto.textContent = "CUPOM DO VENDEDOR (opcional)";
        btn.classList.remove("btn-outline-success");
        btn.classList.add("btn-outline-secondary");
    }
}

async function gerenciarCupomVendedor() {
    // Ja tem cupom informado: deixa manter ou remover.
    if (codigoVendedorInformado) {
        const r = await Swal.fire({
            title: 'CUPOM DO VENDEDOR',
            text: `Vendedor informado: ${codigoVendedorInformado}`,
            showCancelButton: true,
            showDenyButton: true,
            confirmButtonText: 'MANTER',
            denyButtonText: 'REMOVER',
            cancelButtonText: 'CANCELAR'
        });

        if (r.isDenied) {
            codigoVendedorInformado = "";
            atualizarBotaoCupomVendedor();
        }
        return;
    }

    // Sem cupom: pede e valida. Cancelar mantem a venda sem vendedor.
    await pedirCupomVendedor();
}

async function pedirCupomVendedor() {
    while (true) {
        const { value } = await Swal.fire({
            title: 'CÓDIGO DO VENDEDOR',
            input: 'text',
            inputLabel: 'Cupom do vendedor (opcional)',
            inputPlaceholder: 'Digite o código (ex: LEO)',
            showCancelButton: true,
            confirmButtonText: 'Validar',
            cancelButtonText: 'Cancelar',
            allowOutsideClick: false,
            didOpen: () => {
                // O input do Swal pode ainda nao estar no DOM quando didOpen roda;
                // o setTimeout garante que o foco grude e o operador consiga digitar.
                setTimeout(() => {
                    const input = document.querySelector('.swal2-input');
                    if (input) input.focus();
                }, 50);
            },
            inputValidator: (v) => {
                if (!v || !v.trim()) return 'Digite o cupom ou cancele.';
            }
        });

        // Cancelou (ou fechou): a venda segue sem vendedor identificado.
        if (!value) return;

        try {
            const resp = await fetch(`/Caixa/ValidarCupom?codigo=${encodeURIComponent(value.trim())}`);
            const data = await resp.json();

            if (data.exists) {
                codigoVendedorInformado = value.trim();
                atualizarBotaoCupomVendedor();
                return;
            }

            await Swal.fire('Cupom não encontrado', 'Verifique o código e tente novamente.', 'error');
            // Volta ao prompt (loop) ate acertar ou cancelar.
        } catch (e) {
            console.error("Erro validando cupom:", e);
            await Swal.fire('Erro', 'Falha ao validar cupom. Tente novamente.', 'error');
            return;
        }
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
            headers: csrfHeaders({
                'Content-Type': 'application/x-www-form-urlencoded'
            })
        });

        if (!response.ok) {
            const textoErro = await response.text();
            console.log('[ERRO 400] status:', response.status, 'texto:', textoErro.substring(0, 2000));
            throw new Error('Servidor retornou ' + response.status + ': ' + textoErro.substring(0, 200));
        }

        const res = await response.json();

        if (res.success) {
            // Fecha nosso modal custom e garante que não existam backdrops bloqueando
            try { fecharModal(); } catch (e) { /* noop se não existir */ }
            document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
            document.body.classList.remove('modal-open');
            document.body.style.overflow = '';

// Abre SweetAlert garantindo z-index acima do modal
            Swal.fire({
                title: 'Venda finalizada!',
                text: 'Sucesso ao processar a venda.',
                customClass: {
                    popup: 'swal-success'
                },
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
                // BUG-30: o titulo era fixo em "Erro no Estoque", mas a recusa
                // pode ser de dinheiro insuficiente (BUG-29), de cupom, de
                // quantidade ou de estoque. O texto do SERVIDOR e a autoridade
                // e aparece abaixo; o titulo agora so diz que a venda nao saiu.
                title: 'Venda não concluída',
                html: (res.message || 'Não foi possível concluir a venda.').replace(/\n/g, '<br>'),
                customClass: {
                    popup: 'swal-error'
                },
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

// ---------------------------------------------------------------------------
// BUG-30: helpers de dinheiro e do botao CONFIRMAR
// ---------------------------------------------------------------------------

// Converte um texto digitado em CENTAVOS inteiros (ou null se nao for numero).
// Aceita virgula e ponto como separador. Math.round evita o 0.1+0.2 != 0.3 do
// float: sem isso 27,00 viraria 2699.9999 e a comparacao com o total erraria.
function textoParaCentavos(texto) {
    if (texto === null || texto === undefined) return null;
    const limpo = String(texto).trim().replace(',', '.');
    if (limpo === "") return null;
    const n = Number(limpo);
    if (!isFinite(n)) return null;
    return Math.round(n * 100);
}

// Total do carrinho EM CENTAVOS, arredondando item a item. Somar em reais e so
// no fim daria erro acumulado (3x R$ 0,10 daria 0.30000000000000004).
function totalCarrinhoEmCentavos() {
    let totalCentavos = 0;
    (carrinho || []).forEach(item => {
        const precoCentavos = Math.round((parseFloat(String(item.preco ?? item.Preco ?? 0).replace(',', '.')) || 0) * 100);
        const qtdCentavos = Math.round((parseFloat(String(item.qtd ?? item.Quantidade ?? item.quantidade ?? 0).replace(',', '.')) || 0) * 100);
        totalCentavos += Math.round(precoCentavos * qtdCentavos / 100);
    });
    return totalCentavos;
}

function formatarCentavos(centavos) {
    return (centavos / 100).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// Liga/desliga o CONFIRMAR. Usamos disabled (o navegador impede o clique) e
// aria-disabled (o leitor de tela anuncia) - o disabled sozinho nao e anunciado
// por todos os leitores de tela.
function definirConfirmarHabilitado(habilitado) {
    const btn = document.getElementById("btnConfirmarPagamento");
    if (!btn) return;
    btn.disabled = !habilitado;
    if (habilitado) {
        btn.removeAttribute("aria-disabled");
    } else {
        btn.setAttribute("aria-disabled", "true");
    }
    btn.classList.toggle("pdv-btn-bloqueado", !habilitado);
}

// O CONFIRMAR so pode ficar habilitado se NAO for Dinheiro, OU se for Dinheiro
// com valor recebido suficiente. O SERVIDOR continua sendo a autoridade: isto e
// so para o operador nao descobrir a recusa so depois de tentar.
function podeConfirmarPagamento() {
    const metodo = String(metodoSelecionado || "").toLowerCase();
    if (!metodo) return true;                      // sem metodo, quem recusa e o modal
    if (!metodo.includes('dinheiro')) return true; // Pix/Debito/Credito: livre

    const recebido = textoParaCentavos(document.getElementById("valorRecebidoInput")?.value);
    if (recebido === null) return false;           // vazio ou nao numerico
    if (recebido <= 0) return false;               // zero ou negativo
    return recebido >= totalCarrinhoEmCentavos();
}

// Troca o rotulo pequeno (TROCO / FALTAM) e a cor do valor grande.
function definirAvisoTroco(estado, textoValor) {
    const rotulo = document.getElementById("displayTrocoRotulo");
    const displayTroco = document.getElementById("displayTroco");
    if (displayTroco) {
        displayTroco.innerText = textoValor;
        displayTroco.classList.toggle('text-danger', estado === 'falta');
        displayTroco.classList.toggle('text-success', estado !== 'falta');
        displayTroco.classList.toggle('pdv-valor-falta', estado === 'falta');
    }
    if (rotulo) {
        rotulo.textContent = estado === 'falta' ? 'Faltam' : 'Troco';
        rotulo.classList.toggle('pdv-valor-rotulo-falta', estado === 'falta');
    }
}

// Calcula o troco a partir do carrinho e do valor recebido pelo operador.
// BUG-30: toda a comparacao passa a ser feita em CENTAVOS inteiros, e o
// CONFIRMAR e reavaliado a cada digitacao (oninput) e a cada troca de metodo.
function calcularTroco() {
    const valorRecebidoEl = document.getElementById("valorRecebidoInput");
    const displayTroco = document.getElementById("displayTroco");
    const valorTrocoInput = document.getElementById("valorTrocoInput");

    if (!displayTroco || !valorTrocoInput) return;

    const recebidoCentavos = textoParaCentavos(valorRecebidoEl?.value);
    const totalCentavos = totalCarrinhoEmCentavos();

    // BUG-30: o botao e recalculado sempre, mesmo fora de Dinheiro (nesse caso
    // podeConfirmarPagamento devolve true e nada muda visualmente).
    definirConfirmarHabilitado(podeConfirmarPagamento());

    // Campo vazio / nao numerico / carrinho vazio: nao ha "Faltam" a mostrar,
    // porque ainda nao se sabe o que falta - o operador esta no comeco.
    if (recebidoCentavos === null || totalCentavos === 0) {
        definirAvisoTroco('neutro', "R$ 0,00");
        valorTrocoInput.value = "0";
        return;
    }

    const diffCentavos = recebidoCentavos - totalCentavos;

    if (diffCentavos >= 0) {
        // Tem troco para devolver
        definirAvisoTroco('troco', "R$ " + formatarCentavos(diffCentavos));
        valorTrocoInput.value = (diffCentavos / 100).toFixed(2).replace(',', '.');
    } else {
        // Valor insuficiente -> mostra quanto falta, e o total ja esta conhecido
        definirAvisoTroco('falta', "R$ " + formatarCentavos(Math.abs(diffCentavos)));
        valorTrocoInput.value = "0";
    }
}

// Reabre o modal de pagamento SEM estado residual: zera o metodo escolhido, o
// valor digitado, o troco e o aviso. Sem isto, o valor da venda anterior
// continuaria no campo e o CONFIRMAR abriria habilitado para o valor errado.
function resetarEtapaPagamento() {
    metodoSelecionado = "";
    const inputMetodo = document.getElementById("inputMetodoPagamento");
    if (inputMetodo) inputMetodo.value = "";
    document.querySelectorAll('.btn-metodo-sel').forEach(b => b.classList.remove('is-selected'));

    const valRec = document.getElementById("valorRecebidoInput");
    if (valRec) valRec.value = "";

    const hiddenTroco = document.getElementById("valorTrocoInput");
    if (hiddenTroco) hiddenTroco.value = "0";

    const secaoTroco = document.getElementById("secaoTroco");
    if (secaoTroco) secaoTroco.style.display = "none";

    const secaoPagamento = document.getElementById("secaoPagamento");
    const secaoCPF = document.getElementById("secaoCPF");
    if (secaoPagamento) secaoPagamento.style.display = "block";
    if (secaoCPF) secaoCPF.style.display = "none";

    const cpf = document.getElementById("cpfClienteInput");
    if (cpf) cpf.value = "";

    definirAvisoTroco('neutro', "R$ 0,00");
    definirConfirmarHabilitado(true);
}