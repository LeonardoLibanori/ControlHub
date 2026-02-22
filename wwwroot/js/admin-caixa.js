
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
        // 2. Remove o ponto (.) que é separador de milhar
        // 3. Troca a vírgula (,) por ponto (.) que é o separador decimal do C#
        let valorLimpo = valorRaw.replace("R$", "").trim();
        valorLimpo = valorLimpo.split('.').join(''); // Remove pontos de milhar
        valorLimpo = valorLimpo.replace(',', '.');   // Troca vírgula decimal por ponto

        const valorNumerico = parseFloat(valorLimpo);

        if (isNaN(valorNumerico) || valorNumerico <= 0) {
            Swal.fire('Erro', 'Por favor, insira um valor válido!', 'error');
            return;
        }

        const data = new URLSearchParams();
        data.append('tipo', tipo);
        data.append('valor', valorNumerico.toFixed(2)); // Envia sempre formato 100.00
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

        console.log("DADOS QUE CHEGARAM DO C#:", data); // Deve mostrar o 777 aqui!

        if (data.success) {
            // Pegamos o valor exato vindo do Controller
            let estoqueVindoDoServidor = data.estoque ?? 0;

            itemAtual = {
                id: data.id,
                nome: data.nome,
                preco: parseFloat(data.preco),
                regra: data.regra,
                estoque: estoqueVindoDoServidor // AQUI O 777 ENTRA!
            };

            // Preenche o Modal com os dados reais
            if (document.getElementById("modalNomeProduto"))
                document.getElementById("modalNomeProduto").innerText = itemAtual.nome;

            if (document.getElementById("modalEstoqueDisponivel"))
                document.getElementById("modalEstoqueDisponivel").innerText = itemAtual.estoque;

            if (document.getElementById("produtoIdVenda"))
                document.getElementById("produtoIdVenda").value = itemAtual.id;

            // Abre o modal de confirmação
            const modalEl = document.getElementById('modalVendaConfirmacao');
            if (modalEl) {
                new bootstrap.Modal(modalEl).show();
            }

            // Foca na quantidade
            setTimeout(() => {
                const cq = document.getElementById("campoQtd");
                if (cq) { cq.value = "1"; cq.focus(); cq.select(); }
            }, 500);

            // Limpa busca
            document.getElementById("campoBusca").value = "";

        } else {
            Swal.fire('Ops!', 'Produto não encontrado.', 'warning');
            document.getElementById("campoBusca").value = "";
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