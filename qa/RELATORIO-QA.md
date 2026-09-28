# Relatório de QA — Sistema de Controle de Estoque

**Branch:** `fix/fase1-dinheiro` · **HEAD:** `1016148` · **Data da recriação:** 28/09/2026

> Esta suíte foi **recriada do zero** após a troca de máquina. As pastas `tests.QA/` e
> `qa/` existiam apenas no PC antigo e nunca foram versionadas. A partir de agora
> ambas estão no Git (ver `git log`).

## Como executar

```powershell
cd ControleEstoqueLoja\tests.QA
dotnet test
```

Cada teste cria um **banco SQLite isolado em `%TEMP%`** (`qa-teste-<guid>.db`) e o remove
ao final. **O `controleestoque.db` versionado nunca é aberto pela suíte.**

## Estado atual

| Métrica | Valor |
|---|---|
| Total de testes | **55** |
| ✅ Aprovados (comportamento correto) | **32** |
| ❌ Falhando (bug presente) | **23** |

Os testes são de **caracterização**: falham enquanto o bug existir e passam quando for
corrigido. Nenhum código de produção foi alterado para fazê-los passar.

---

## Severidade Crítica

### BUG-01 — Sinal da sangria invertido no fechamento
`CaixaController.ObterDadosFechamento` (linha 456)

```csharp
decimal saldoFinal = (totalVendas + suprimentos) - sangrias;
```

`RegistrarMovimentacao` grava a sangria **já negativa** (`-Math.Abs(valor)`). A subtração
inverte o sinal e a sangria **soma** ao saldo. Ex.: venda R$ 100 + sangria R$ 50
→ esperado R$ 50, obtido **R$ 150**. O fechamento superestima o dinheiro da gaveta.

→ `CaixaQaTests.ObterDadosFechamento_DeveSubtrairSangriaDoSaldo`

### BUG-02 / 03 / 04 — Saldo em gaveta inclui todas as formas de pagamento
`ObterStatusAtual`, `RelatorioDados` e `ObterDadosFechamento`

O saldo físico da gaveta soma `Vendas.TotalVenda` **sem filtrar `MetodoPagamento`**. Uma
venda de R$ 500 no cartão entra no saldo como se houvesse R$ 500 em dinheiro.
**Complemento A:** o saldo deve considerar **apenas `Dinheiro`**.

→ `CaixaQaTests.ObterStatusAtual_SaldoDeveConsiderarSomenteDinheiro`
→ `CaixaQaTests.RelatorioDados_SaldoFinalDeveConsiderarSomenteDinheiro`
→ `CaixaQaTests.ObterDadosFechamento_SaldoDeveConsiderarSomenteDinheiro`

### BUG-07 — Editar produto zera o estoque
`HomeController.Editar` (linha 134)

```csharp
decimal.TryParse(quantidade?..., out decimal qtd);   // retorno ignorado
produto.Quantidade = qtd;                            // = 0 quando a view não envia
```

`Views/Home/Editar.cshtml` não tem o campo `quantidade`, então `qtd` vira **0** e salvar a
edição **apaga todo o estoque**, silenciosamente.

→ `ProdutosQaTests.Editar_DevePreservarEstoque_QuandoCampoQuantidadeNaoEnviado`

### BUG-08 — `ON DELETE CASCADE` apaga histórico de vendas
`Migrations/20260220171624_Inicial_Nova.cs` (linhas 111–116)

`FK_VendaItens_Produtos_ProdutoId` usa `ReferentialAction.Cascade`. Ao excluir um produto o
banco apaga **todos os `VendaItens`** dele — o histórico financeiro é perdido e o relatório
do dia deixa de bater com o extrato.

→ `ProdutosQaTests.Excluir_DevePreservarHistoricoDeVendas_DoProduto`

### BUG-12 — XSS armazenado no PDV
`HomeController.Novo` + `wwwroot/js/admin-caixa.js` (linha 438)

O nome do produto é gravado sem sanitizar e interpolado direto no `innerHTML`:

```javascript
btn.innerHTML = `<strong>${nomeOriginal}</strong> <small ...>R$ ${div.dataset.preco}</small>`;
```

Um produto chamado `<img src=x onerror=alert(1)>` executa JavaScript no navegador de quem
busca no PDV. A view Razor escapa (`@Model.Nome`); o JavaScript não.

---

## Severidade Alta

| ID | Descrição | Teste |
|---|---|---|
| BUG-05 | `RegistrarMovimentacao` aceita sangria maior que o saldo (sem validação de saldo) | `CaixaQaTests.RegistrarMovimentacao_DeveRejeitarSangriaMaiorQueOSaldo` |
| BUG-06 | `RegistrarMovimentacao` aceita valor `<= 0`, gerando movimentação fantasma | `CaixaQaTests.RegistrarMovimentacao_DeveRejeitarValorNaoPositivo` |
| BUG-09 | Cadastro aceita código de barras duplicado | `ProdutosQaTests.Novo_DeveRejeitarCodigoDeBarrasDuplicado` |
| BUG-10 | Cadastro aceita preço negativo | `ProdutosQaTests.Novo_DeveRejeitarPrecoNegativo` |
| BUG-11 | Cadastro aceita estoque negativo | `ProdutosQaTests.Novo_DeveRejeitarEstoqueNegativo` |
| BUG-14 | POSTs sem `[ValidateAntiForgeryToken]` (CSRF) | `SegurancaQaTests.PostActions_NaoDevemFaltarAntiForgery` |
| BUG-18 | Case-sensitivity entre `ValidarCupom` (`.ToUpper()`) e `FinalizarVenda` (`==`): no SQLite o `=` é case-sensitive, então o cupom é **validado** e depois **rejeitado** | `VendedoresQaTests.Cupom_DeveAceitarMesmoCodigoEmCaixaDiferente_NasDuasEtapas` |
| BUG-19 | `Program.cs` usa `EnsureCreated()` em vez de `Migrate()` — migrations novas nunca são aplicadas em base existente | `DadosQaTests.Program_DeveUsarMigrate_EmVezDeEnsureCreated` |
| BUG-20 | `decimal` mapeado como `TEXT` no SQLite — `ORDER BY` fica lexicográfico (`"10,00" < "9,00"`), quebrando relatórios | `DadosQaTests.Decimal_NaoDeveSerPersistidoComoText` |

---

## Severidade Média

| ID | Descrição | Teste |
|---|---|---|
| BUG-15 | `ListarVendedores` vaza `ex.Message` ao cliente | `SegurancaQaTests.ListarVendedores_NaoDeveVazarMensagemDeExcecao` |
| BUG-16 | `ObterDadosFechamento` vaza `ex.Message` ao cliente | `SegurancaQaTests.ObterDadosFechamento_NaoDeveVazarMensagemDeExcecao` |
| BUG-17 | `RegistrarMovimentacao` vaza `FormatException` ao cliente | `SegurancaQaTests.RegistrarMovimentacao_NaoDeveVazarMensagemDeExcecao` |
| BUG-21 | `TempData["Erro"]` definido em `AjustarEstoque` **nunca é exibido** — o usuário recebe a tela sem aviso | `LacunasQaTests.TempDataErro_DeveSerExibidoNaView` |
| BUG-22 | `HomeController` não tem a action `Error`, mas `Program.cs` aponta `UseExceptionHandler("/Home/Error")` → **404** em produção | `LacunasQaTests.HomeController_DevePossuirActionError` |

> **Filtro de data do Histórico:** `CaixaController.Historico` **aplica corretamente** o
> filtro no controller, então `LacunasQaTests.Historico_DeveRespeitarOFiltroDeData` **passa**.
> O sintoma percebido na tela está na camada de rota/view e não é coberto por teste
> unitário — ver `qa/http-parte1.ps1`.

---

## Já corrigido (Fase 1 — commit `1016148`)

Preço de tabela ignorando o POST, quantidade `<= 0` rejeitada, granel `x10` via
`CalcularSubtotalItem`, troco recalculado no servidor, produto/vendedor inexistente
rejeitado, estoque insuficiente barrado, sangria gravada negativa e fechamento idempotente.
São os 32 testes verdes — ver `qa/falhas.csv` (`Status = corrigido`).


→ `SegurancaQaTests.NomeDeProduto_ComScript_DeveSerSanitizadoParaOInnerHTML`

### BUG-13 — Nenhum endpoint tem autenticação
Nenhum controller possui `[Authorize]`. Qualquer pessoa na mesma rede acessa
`/Caixa/Index`, `/Home/Excluir`, `/Caixa/RegistrarMovimentacao`, `/Caixa/ExcluirVendedor`.

→ `SegurancaQaTests.Controllers_NaoDevemExporEndpointsSemAutorizacao`
