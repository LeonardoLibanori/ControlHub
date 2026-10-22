
# ControlHub — Controle de Estoque e Caixa

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Entity Framework Core](https://img.shields.io/badge/EF%20Core-10.0-3178C6)
![SQLite](https://img.shields.io/badge/SQLite-3-003B57)
![Testes](https://img.shields.io/badge/testes-373-brightgreen)

Sistema web de **gestão de estoque e ponto de venda (PDV)** para pequenas lojas: vender no balcão, controlar estoque (por unidade ou por peso), acompanhar o faturamento e fechar o caixa — tudo em português, com tema claro/escuro e atalhos de teclado.

Desenvolvido em **ASP.NET Core MVC + Entity Framework Core (SQLite)**, com suíte de **373 testes automatizados**.

---

## 🔑 Acesso de teste

**Demo on-line:** <https://controle-estoque-loja.runasp.net>

Contas de demonstração para avaliação do projeto:

| Perfil | E-mail | Senha |
|---|---|---|
| **Administrador (Dono)** | `admin@loja.com` | `Admin@321` |
| **Operador** | `operador1@loja.com` | `Operador1@321` |

> O **Dono** acessa tudo (financeiro, estoque, usuários e dashboard). O **Operador** acessa o PDV e a consulta de estoque — o restante é bloqueado por política de autorização, de propósito: dá para ver a separação de permissões funcionando em tempo real.

> Rodando localmente: em um clone novo, o banco nasce vazio. Se as contas ainda não existirem, crie o Dono em `/Account/Setup` (a tela só aparece com o banco vazio) e o Operador em **Usuários**.

---

## ▶️ Rodando localmente

**Pré-requisito:** [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
git clone <url-do-repositorio>
cd <pasta-do-repositorio>
dotnet run
```

Abra <http://localhost:5236> e entre com as credenciais acima.

- Na **primeira execução** o banco `controleestoque.db` é criado automaticamente (migrations) — nenhum arquivo de banco precisa ser baixado ou copiado.
- Com o banco vazio, o sistema redireciona para `/Account/Setup`, onde você cria a conta do Dono. A tela some assim que existe pelo menos um usuário.

---

## ✨ O que o sistema faz

### 🛒 Caixa (PDV)

- Carrinho de vendas com busca por descrição, código e **código de barras / balança**
- Venda **por unidade ou por peso** (preço em R$/kg)
- Cinco formas de pagamento — Dinheiro, Pix, Débito, Crédito e Voucher — com troco e resumo por forma
- CPF na nota e **cupom do vendedor** validado no campo (verde quando aceito, vermelho quando não)
- Impressão e **reimpressão de cupom** de qualquer venda
- Sangria, suprimento, status e fechamento de caixa
- Menu administrativo completo no atalho **Alt+M**

### 📦 Estoque

- Cadastro de produtos com unidade, estoque mínimo e código de barras
- Alerta de **estoque baixo** e destaque para produtos zerados
- **Exclusão lógica** — nada é apagado fisicamente do histórico
- Exportação **CSV e Excel** com linha de totais

### 📊 Dashboard

- Faturamento do período com comparação ao período anterior
- Série diária em **SVG puro** (7/30 dias, mês, ano)
- **Rosca de formas de pagamento** — cor por forma, fatias começando no topo e soma conferida em 100,0%
- Estoque baixo e top 5 mais vendidos
- Detalhe de cada dia com um clique

### 🔐 Acesso e interface

- Papéis **Dono** e **Operador** com políticas de autorização em fonte única
- Tema claro/escuro e layout responsivo (gaveta lateral no celular)
- Atalhos: `Alt+M` menu · `Alt+C` caixa · `Alt+H` histórico · `Alt+E` estoque · `ESC` ajuda

---

## 🎯 Diferenciais técnicos

| Diferencial | Como é feito |
|---|---|
| **Permissões em fonte única** | Policies nomeadas em `Security/Policies.cs` — o mesmo nome usado em action, teste e view; “o Operador vê X?” tem **uma** resposta |
| **Dinheiro sem ponto flutuante** | Cálculos em **centavos** (inteiro) — nada de erro de arredondamento em operação de caixa |
| **Servidor é a autoridade** | Todas as validações no back-end; o front só informa — o cliente nunca decide |
| **CSRF em todo POST** | Token anti-forgery no header de todas as chamadas `fetch()` e nos formulários Razor |
| **Bloqueio de conta** | 5 tentativas erradas = 5 minutos de bloqueio (Identity) |
| **Testes de caracterização** | 373 testes xUnit com SQLite descartável em `%TEMP%` — o banco real nunca é tocado |
| **QA visual automatizado** | Guards em Node/PowerShell (`qa/`): contraste WCAG, encoding, CSS balanceado, menu por papel |
| **Números e acentos** | SVG com `InvariantCulture` (vírgula brasileira garantida) e textos em pt-BR |

---

## 🛠 Tecnologias

- **.NET 10** / ASP.NET Core MVC
- **Entity Framework Core 10** (Code-First, migrations)
- **SQLite**
- **ASP.NET Core Identity** (papéis, lockout, cookies)
- Razor Views com Runtime Compilation
- Bootstrap + SweetAlert2 no front
- **xUnit** na suíte de testes

---

## 🧪 Testes

```powershell
dotnet test tests.QA\tests.QA.csproj
```

- **373 testes**: regras de negócio, dashboard, formas de pagamento, autorização, exportações e mais
- Cada teste cria um SQLite descartável em `%TEMP%`
- Achados de QA: [`qa/RELATORIO-QA.md`](qa/RELATORIO-QA.md) e [`qa/falhas.csv`](qa/falhas.csv)

---

## 🗂 Estrutura

```
├── Controllers/       Home (estoque), Caixa (PDV), Dashboard, Usuários...
├── Models/            Produto, Venda, VendaItem, Vendedor, movimentações
├── Security/          Policies.cs (papéis e permissões) e eventos de cookie
├── Data/              ApplicationDbContext
├── Migrations/        migrations do EF Core
├── Views/             Razor (Caixa, Dashboard, Home, Shared...)
├── wwwroot/           CSS/JS próprios (corporate-theme, dashboard, shell)
├── tests.QA/          suíte xUnit (373 testes)
├── qa/                guards visuais, scripts HTTP e relatórios de QA
└── .github/           instruções do Copilot
```

---

## 🔮 Próximos passos — Copiloto de IA

O próximo passo é um **copiloto de IA** para o Dono conversar com os próprios dados — “quanto vendi esta semana?”, “o que está parado no estoque?”, “qual a forma de pagamento mais usada este mês?”.

O caminho já está trilhado **sem mexer no que existe**:

1. A policy **`PodeUsarIA`** já está declarada e registrada em `Program.cs` (somente Dono). Hoje **nenhuma action a usa** — é a trava de espera.
2. Os endpoints de IA virão protegidos por `[Authorize(Policy = Policies.PodeUsarIA)]`, seguindo o mesmo padrão das demais rotas.
3. **Nenhuma rota de produção existente muda** e nenhum dado de financeiro/estoque fica acessível sem a permissão nova passar primeiro pela revisão.

---

## ⚠️ Observações

- Os **dados fiscais impressos no cupom** (CNPJ, endereço, telefone) são fictícios e precisam ser parametrizados antes de qualquer uso comercial.
- O **banco de dados não é versionado** pelo Git: ele nasce vazio na primeira execução e os dados ficam em cada máquina.
- O histórico deste repositório foi reescrito — SHAs antigos não existem mais.

---

<p align="center">Desenvolvido com C# e .NET 10 🚀</p>

