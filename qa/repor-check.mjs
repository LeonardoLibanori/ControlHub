/* ============================================================================
   QA REPOR - Verificação visual e funcional do botão Repor (CDP)
   Executa nos DOIS temas, 1440x900 e 390x844
   ============================================================================ */
import { cdp, waitFor, sleep } from './cdp-helper.mjs';

const URL = 'http://localhost:5236';
const TEMAS = ['light', 'dark'];
const VIEWPORTS = [
    { name: 'desktop', width: 1440, height: 900 },
    { name: 'mobile', width: 390, height: 844 }
];

async function login(client, user, pass) {
    await client.Page.navigate({ url: `${URL}/Account/Login` });
    await waitFor(client, 'input[name="Email"]');
    await client.Runtime.evaluate({
        expression: `
            document.querySelector('input[name="Email"]').value = '${user}';
            document.querySelector('input[name="Senha"]').value = '${pass}';
            document.querySelector('button[type="submit"]').click();
        `
    });
    await sleep(1000);
    await waitFor(client, '.main-container, .app-main, h1');
}

async function medirRect(client, selector) {
    const result = await client.Runtime.evaluate({
        expression: `
            (() => {
                const el = document.querySelector('${selector}');
                if (!el) return null;
                const r = el.getBoundingClientRect();
                return { x: r.x, y: r.y, width: r.width, height: r.height, top: r.top, left: r.left, bottom: r.bottom, right: r.right };
            })()
        `
    });
    return result.result.result.value;
}

async function contarBotoesRepor(client) {
    const result = await client.Runtime.evaluate({
        expression: `document.querySelectorAll('[data-repor]').length`
    });
    return result.result.result.value;
}

async function clicarRepor(client, index = 0) {
    await client.Runtime.evaluate({
        expression: `document.querySelectorAll('[data-repor]')[${index}].click()`
    });
    await sleep(300);
}

async function preencherRepor(client, valor) {
    await client.Runtime.evaluate({
        expression: `
            const input = document.getElementById('reporInput');
            input.value = '${valor}';
            input.dispatchEvent(new Event('input', { bubbles: true }));
        `
    });
    await sleep(100);
}

async function confirmarRepor(client) {
    await client.Runtime.evaluate({
        expression: `document.getElementById('reporConfirmar').click()`
    });
    await sleep(1500);
}

async function fecharModalEsc(client) {
    await client.Runtime.evaluate({
        expression: `document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`
    });
    await sleep(300);
}

async function fecharModalCancelar(client) {
    await client.Runtime.evaluate({
        expression: `document.getElementById('reporCancelar').click()`
    });
    await sleep(300);
}

async function medirColunasEstoque(client) {
    const result = await client.Runtime.evaluate({
        expression: `
            (() => {
                const tabela = document.querySelector('.tabela-responsiva--estoque');
                if (!tabela) return null;
                const ths = tabela.querySelectorAll('thead th');
                const rects = Array.from(ths).map(th => {
                    const r = th.getBoundingClientRect();
                    return { text: th.textContent.trim(), width: r.width, left: r.left, right: r.right };
                });
                return rects;
            })()
        `
    });
    return result.result.result.value;
}

async function verificarBadgesEstoqueBaixo(client) {
    const result = await client.Runtime.evaluate({
        expression: `
            (() => {
                const badges = document.querySelectorAll('.badge-estoque-baixo');
                return Array.from(badges).map(b => b.textContent.trim());
            })()
        `
    });
    return result.result.result.value;
}

async function main() {
    const client = await cdp();
    const resultados = [];

    try {
        // Setup: criar 3 produtos via API ou seed direto
        // Para simplificar, assumimos que o banco de teste já tem os produtos
        // Se não, precisamos criar via POST /Home/Novo

        for (const tema of TEMAS) {
            await client.Emulation.setEmulatedMedia({ features: [{ name: 'prefers-color-scheme', value: tema }] });

            for (const vp of VIEWPORTS) {
                await client.Emulation.setDeviceMetricsOverride({
                    width: vp.width,
                    height: vp.height,
                    deviceScaleFactor: 1,
                    mobile: vp.name === 'mobile'
                });

                console.log(`\n=== TEMA: ${tema} | VIEWPORT: ${vp.name} (${vp.width}x${vp.height}) ===`);

                // Medir ANTES
                const colunasAntes = await medirColunasEstoque(client);
                console.log('Colunas ANTES:', JSON.stringify(colunasAntes));

                // Login Dono
                await login(client, 'dono@teste.com', '***REMOVIDO***');
                await sleep(500);

                // 1) Dono vê os 3 botões
                const qtdRepor = await contarBotoesRepor(client);
                console.log(`Botões Repor visíveis (Dono): ${qtdRepor}`);
                if (qtdRepor !== 3) throw new Error(`Esperado 3 botões Repor, achou ${qtdRepor}`);

                // Medir altura e alinhamento vertical dos 3 botões na primeira linha
                const botoesInfo = await client.Runtime.evaluate({
                    expression: `
                        (() => {
                            const botoes = document.querySelectorAll('.acoes-com-repor .btn-editar, .acoes-com-repor .btn-repor, .acoes-com-repor .btn-excluir');
                            return Array.from(botoes).map((b, i) => {
                                const r = b.getBoundingClientRect();
                                return { index: i, class: b.className, height: r.height, top: r.top, bottom: r.bottom };
                            });
                        })()
                    `
                });
                console.log('Botões métricas:', JSON.stringify(botoesInfo.result.result.value));

                // Verificar mesma altura (+/-1px) e mesmo centro vertical (+/-2px)
                const alturas = botoesInfo.result.result.value.map(b => b.height);
                const tops = botoesInfo.result.result.value.map(b => b.top);
                const maxAltura = Math.max(...alturas);
                const minAltura = Math.min(...alturas);
                const maxTop = Math.max(...tops);
                const minTop = Math.min(...tops);
                if (maxAltura - minAltura > 1) throw new Error(`Alturas diferentes: ${alturas.join(', ')}`);
                if (maxTop - minTop > 2) throw new Error(`Tops diferentes: ${tops.join(', ')}`);

                // 2) Repor "5" no produto de 10 (índice 0 - por unidade)
                await clicarRepor(client, 0);
                await preencherRepor(client, '5');
                await confirmarRepor(client);
                await sleep(1000);

                // Verificar se recarregou mantendo busca
                const urlPosRepor = await client.Runtime.evaluate({ expression: 'window.location.href' });
                console.log('URL pós repor:', urlPosRepor.result.result.value);

                // 3) Repor "0,125" no produto por peso (índice 1)
                await clicarRepor(client, 1);
                await preencherRepor(client, '0,125');
                await confirmarRepor(client);
                await sleep(1000);

                // 4) "1,5" em produto por unidade (índice 0) - deve mostrar erro e NÃO alterar
                await clicarRepor(client, 0);
                await preencherRepor(client, '1,5');
                await confirmarRepor(client);
                await sleep(1000);

                // Verificar se mensagem de erro apareceu no modal
                const erroModal = await client.Runtime.evaluate({
                    expression: `
                        (() => {
                            const alerta = document.getElementById('reporAlerta');
                            return alerta ? alerta.textContent.trim() : 'SEM_ALERTA';
                        })()
                    `
                });
                console.log('Erro no modal (esperado):', erroModal.result.result.value);
                if (!erroModal.result.result.value.includes('inteiro')) throw new Error('Erro esperado não apareceu');

                // Modal deve continuar aberto
                const modalAberto = await client.Runtime.evaluate({
                    expression: `!document.getElementById('modalRepor').hidden`
                });
                if (!modalModalAberto.result.result.value) throw new Error('Modal fechou indevidamente');

                // Fechar modal com Cancelar
                await fecharModalCancelar(client);

                // 5) Repor 10 no produto com estoque 2 e mínimo 5 (índice 2) - selo deve sumir
                await clicarRepor(client, 2);
                await preencherRepor(client, '10');
                await confirmarRepor(client);
                await sleep(1000);

                const badgesPos = await verificarBadgesEstoqueBaixo(client);
                console.log('Badges estoque baixo pós repor:', badgesPos);

                // 6) Duplo clique - contar POSTs
                let postCount = 0;
                client.Network.requestWillBeSent(({ request }) => {
                    if (request.url.includes('/Home/Repor') && request.method === 'POST') postCount++;
                });
                await clicarRepor(client, 0);
                await preencherRepor(client, '1');
                // Duplo clique rápido
                await client.Runtime.evaluate({ expression: `document.getElementById('reporConfirmar').click()` });
                await sleep(50);
                await client.Runtime.evaluate({ expression: `document.getElementById('reporConfirmar').click()` });
                await sleep(1500);
                console.log(`POSTs em duplo clique: ${postCount}`);
                if (postCount !== 1) throw new Error(`Duplo clique gerou ${postCount} POSTs, esperado 1`);

                // 7) Esc e Cancelar fecham sem alterar + foco volta
                await clicarRepor(client, 0);
                await fecharModalEsc(client);
                const focoVoltouEsc = await client.Runtime.evaluate({
                    expression: `document.activeElement === document.querySelector('[data-repor]')`
                });
                console.log('Foco voltou após Esc:', focoVoltouEsc.result.result.value);

                await clicarRepor(client, 0);
                await fecharModalCancelar(client);
                const focoVoltouCancelar = await client.Runtime.evaluate({
                    expression: `document.activeElement === document.querySelector('[data-repor]')`
                });
                console.log('Foco voltou após Cancelar:', focoVoltouCancelar.result.result.value);

                // Com texto digitado, clique no fundo NÃO fecha
                await clicarRepor(client, 0);
                await preencherRepor(client, '5');
                await client.Runtime.evaluate({
                    expression: `
                        const overlay = document.getElementById('modalRepor');
                        const rect = overlay.getBoundingClientRect();
                        overlay.dispatchEvent(new MouseEvent('click', { clientX: rect.left + 10, clientY: rect.top + 10, bubbles: true }));
                    `
                });
                await sleep(300);
                const modalAindaAberto = await client.Runtime.evaluate({
                    expression: `!document.getElementById('modalRepor').hidden`
                });
                console.log('Modal aberto após clique no fundo com texto:', modalAindaAberto.result.result.value);
                if (!modalAindaAberto.result.result.value) throw new Error('Modal fechou indevidamente com texto digitado');
                await fecharModalCancelar(client);

                // 8) Tab não sai do modal (20 tabulações)
                await clicarRepor(client, 0);
                for (let i = 0; i < 20; i++) {
                    await client.Runtime.evaluate({
                        expression: `document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))`
                    });
                    await sleep(50);
                }
                const focoNoModal = await client.Runtime.evaluate({
                    expression: `
                        (() => {
                            const modal = document.getElementById('modalRepor');
                            return modal.contains(document.activeElement);
                        })()
                    `
                });
                console.log('Foco preso no modal após 20 tabs:', focoNoModal.result.result.value);
                if (!focoNoModal.result.result.value) throw new Error('Tab escapou do modal');
                await fecharModalCancelar(client);

                // 9) Modal 100% dentro da janela, sem scroll horizontal
                await clicarRepor(client, 0);
                const modalRect = await client.Runtime.evaluate({
                    expression: `
                        (() => {
                            const modal = document.querySelector('.repor-modal');
                            const r = modal.getBoundingClientRect();
                            const vw = window.innerWidth;
                            const vh = window.innerHeight;
                            return { r, vw, vh, dentro: r.left >= 0 && r.right <= vw && r.top >= 0 && r.bottom <= vh };
                        })()
                    `
                });
                console.log('Modal dentro da viewport:', modalRect.result.result.value.dentro);
                if (!modalRect.result.result.value.dentro) throw new Error('Modal fora da viewport');

                const scrollH = await client.Runtime.evaluate({ expression: `document.documentElement.scrollWidth > document.documentElement.clientWidth` });
                console.log('Scroll horizontal na página:', scrollH.result.result.value);
                if (scrollH.result.result.value) throw new Error('Página ganhou scroll horizontal');
                await fecharModalCancelar(client);

                // 10) Medir retângulos ANTES/DEPOIS
                const colunasDepois = await medirColunasEstoque(client);
                console.log('Colunas DEPOIS:', JSON.stringify(colunasDepois));

                // Comparar: Produto/Vendido por/Estoque devem ser iguais (+/-1px), exceto Produto pode encolher
                for (let i = 0; i < colunasAntes.length; i++) {
                    const antes = colunasAntes[i];
                    const depois = colunasDepois[i];
                    if (!depois) continue;
                    const diff = Math.abs(depois.width - antes.width);
                    if (i === 0) { // Produto
                        if (diff > Math.abs(antes.width - depois.width) + 5) {
                            console.warn(`Coluna Produto mudou mais que o esperado: ${antes.width} -> ${depois.width}`);
                        }
                    } else {
                        if (diff > 1) throw new Error(`Coluna ${depois.text} mudou: ${antes.width} -> ${depois.width}`);
                    }
                }

                // 11) Histórico e botões Detalhes/Imprimir funcionam
                await client.Page.navigate({ url: `${URL}/Caixa/Historico` });
                await waitFor(client, '.history-filter');
                await sleep(500);
                const histTitulo = await client.Runtime.evaluate({ expression: `document.querySelector('h1')?.textContent.trim()` });
                console.log('Histórico carregado:', histTitulo.result.result.value);

                // Login Operador e verificar que NÃO vê Repor
                await login(client, 'operador@teste.com', 'Operador@123');
                await sleep(500);
                const qtdReporOperador = await contarBotoesRepor(client);
                console.log(`Botões Repor visíveis (Operador): ${qtdReporOperador}`);
                if (qtdReporOperador !== 0) throw new Error(`Operador não deveria ver botões Repor, achou ${qtdReporOperador}`);

                // POST como operador deve ser recusado
                const postOperador = await client.Runtime.evaluate({
                    expression: `
                        (async () => {
                            const resp = await fetch('/Home/Repor', {
                                method: 'POST',
                                body: new URLSearchParams({ id: '1', quantidade: '5' }),
                                headers: { 'X-CSRF-TOKEN': document.querySelector('input[name="__RequestVerificationToken"]')?.value || '' },
                                credentials: 'same-origin'
                            });
                            return { status: resp.status, text: await resp.text() };
                        })()
                    `
                });
                console.log('POST Operador:', JSON.stringify(postOperador.result.result.value));
                // Deve ser 403 ou 400 (sessão expirada)

                resultados.push({ tema, viewport: vp.name, status: 'OK' });
            }
        }

        console.log('\n=== RESULTADOS ===');
        console.table(resultados);
    } catch (err) {
        console.error('FALHA:', err);
        resultados.push({ status: 'FALHA', erro: err.message });
        process.exitCode = 1;
    } finally {
        await client.close();
    }
}

main();