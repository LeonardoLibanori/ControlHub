# ControlHub — Controle de Estoque e Caixa

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Entity Framework Core](https://img.shields.io/badge/EF%20Core-10.0-3178C6)
![SQLite](https://img.shields.io/badge/SQLite-3-003B57)
![Testes](https://img.shields.io/badge/testes-400%2B-brightgreen)

Sistema web de **gestão de estoque e ponto de venda (PDV)** para pequenas lojas. Permite registrar vendas, controlar estoque por unidade ou peso, acompanhar faturamento, fechar o caixa e consultar movimentações comerciais em uma interface em português, com tema claro/escuro e atalhos de teclado.

Desenvolvido com **ASP.NET Core MVC, Entity Framework Core e SQLite**, com autenticação, controle de acesso por perfil, histórico de movimentações e testes automatizados.

---

## 🔑 Acesso de demonstração

**Demo on-line:** https://controle-estoque-loja.runasp.net

Contas de demonstração para avaliação do projeto:

| Perfil | E-mail | Senha |
|---|---|---|
| **Administrador (Dono)** | `admin@loja.com` | `Admin@321` |
| **Operador** | `operador1@loja.com` | `Operador1@321` |

> O **Dono** possui acesso às funcionalidades administrativas, financeiras e de estoque. O **Operador** tem acesso ao PDV e às consultas permitidas pelas políticas de autorização.

> **Importante:** confirme se essas credenciais ainda existem no ambiente publicado antes de disponibilizá-las publicamente. Em um clone novo, o banco nasce vazio; a conta inicial pode ser criada em `/Account/Setup`, enquanto a configuração inicial estiver disponível.

---

## ▶️ Executando localmente

**Pré-requisito:** [.NET SDK 10](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/LeonardoLibanori/ControlHub.git
cd ControlHub
dotnet run
```

Abra o endereço local exibido pela aplicação no terminal. No ambiente de desenvolvimento utilizado pelo projeto, a porta padrão é `5236`.

- Na primeira execução, o Entity Framework Core utiliza as migrations para preparar o banco SQLite `controleestoque.db`.
- O banco de dados não é versionado pelo Git.
- Em um banco vazio, crie a conta do Dono em `/Account/Setup`, caso a tela de configuração inicial esteja disponível.
- Após criar o usuário administrador, cadastre os demais usuários e produtos conforme necessário.

---

## ✨ Funcionalidades

### 🛒 Caixa e ponto de venda (PDV)

- Carrinho de vendas com busca por descrição, código e código de barras.
- Venda de produtos por unidade ou peso, com cálculo baseado no preço por quilograma quando aplicável.
- Formas de pagamento: dinheiro, Pix, débito, crédito e voucher.
- Cálculo de troco e resumo por forma de pagamento.
- Registro de CPF do cliente e validação do cupom do vendedor.
- Impressão e reimpressão de cupons de venda.
- Operações de sangria e suprimento.
- Acompanhamento do estado e fechamento do caixa.
- Atalho `Alt+M` para o menu administrativo.

### 📦 Estoque e produtos

- Cadastro e edição de produtos com código de barras, unidade de medida, estoque mínimo e custo de aquisição.
- Controle de produtos vendidos por unidade ou peso.
- Alertas de estoque baixo e identificação de produtos sem saldo.
- Exclusão lógica de produtos, preservando os registros históricos relacionados.
- Exportação de dados para CSV e Excel, incluindo totais.
- Registro de entradas, saídas e ajustes no histórico de movimentações de estoque.

### 📚 Kardex e auditoria comercial

- Histórico de movimentações de estoque por produto.
- Registro de entradas, saídas e ajustes de estoque.
- Associação das movimentações aos eventos comerciais que as originam, conforme o fluxo implementado.
- Registro de auditoria de ações comerciais relevantes.
- Consulta de registros com filtros e paginação.

O Kardex ajuda a rastrear as alterações de estoque, enquanto a auditoria comercial mantém registros das ações relevantes realizadas no sistema.

### 📈 Dashboard e rentabilidade

- Faturamento do período com comparação ao período anterior.
- Gráfico diário em SVG, com diferentes intervalos de consulta.
- Distribuição das vendas por forma de pagamento.
- Indicadores de estoque baixo e produtos mais vendidos.
- Detalhamento das vendas por dia.
- Cadastro do custo de aquisição para apoiar análises de rentabilidade.

### 🔐 Autenticação, autorização e interface

- Perfis **Dono** e **Operador**, com políticas de autorização centralizadas.
- Autenticação baseada em ASP.NET Core Identity.
- Bloqueio de conta após tentativas de login malsucedidas.
- Proteção antifalsificação (anti-forgery) em formulários e requisições compatíveis.
- Cookies e cabeçalhos de segurança configurados para o ambiente de execução.
- Tratamento global de erros e registro estruturado de exceções.
- Verificação de saúde da aplicação e persistência de chaves do Data Protection.
- Tema claro/escuro e layout responsivo, incluindo navegação lateral em telas menores.
- Atalhos: `Alt+M` menu · `Alt+C` caixa · `Alt+H` histórico · `Alt+E` estoque · `ESC` ajuda.

---

## 🎯 Diferenciais técnicos

| Diferencial | Implementação |
|---|---|
| **Autorização centralizada** | Policies nomeadas em `Security/Policies.cs`, reutilizadas nos pontos de autorização da aplicação. |
| **Valores monetários consistentes** | Cálculos financeiros baseados em centavos inteiros nos fluxos implementados, reduzindo problemas de arredondamento. |
| **Validação no servidor** | Regras de negócio e validações críticas executadas no back-end, sem depender exclusivamente dos dados do navegador. |
| **Proteção contra CSRF** | Tokens antifalsificação em formulários e requisições que exigem essa proteção. |
| **Autenticação e bloqueio** | ASP.NET Core Identity, configuração de cookies e política de bloqueio de login. |
| **Persistência com EF Core** | Modelo Code-First, migrations e armazenamento SQLite. |
| **Kardex e auditoria** | Registro de movimentações de estoque e ações comerciais relevantes. |
| **Testes automatizados** | Suíte xUnit com banco SQLite descartável para isolar os testes do banco de dados da aplicação. |
| **Integração contínua** | Workflow do GitHub Actions para restaurar dependências, compilar, executar testes e publicar o relatório de testes como artefato. |
| **QA visual automatizado** | Scripts e verificações auxiliares em `qa/` para aspectos de interface, codificação e permissões. |
| **Localização brasileira** | Interface em português, formatação monetária e apresentação de números compatíveis com pt-BR. |

---

## 🛠 Tecnologias

- **C# e .NET 10**
- **ASP.NET Core MVC**
- **Entity Framework Core 10**
- **SQLite**
- **ASP.NET Core Identity**
- **Razor Views**
- **Bootstrap**
- **SweetAlert2**
- **JavaScript e CSS**
- **xUnit**
- **GitHub Actions**
- **Node.js e PowerShell** para verificações auxiliares de QA

---

## 🧪 Testes e qualidade

Execute a suíte automatizada com:

```powershell
dotnet test tests.QA/tests.QA.csproj -c Release
```

A suíte cobre regras de negócio, operações de estoque, vendas, autorização, dashboard e outros comportamentos da aplicação.

- Os testes utilizam bancos SQLite descartáveis para evitar alterações no banco de dados real.
- O workflow de integração contínua executa as etapas de restauração, compilação e testes.
- Verificações auxiliares de qualidade estão organizadas no diretório `qa/`.
- Relatórios e achados de QA podem ser consultados em [`qa/RELATORIO-QA.md`](qa/RELATORIO-QA.md) e [`qa/falhas.csv`](qa/falhas.csv), caso esses arquivos continuem presentes na versão atual do repositório.

**Contagem de testes:** atualize o badge e esta seção com o número efetivamente confirmado na última execução da suíte completa. Não use a contagem histórica de 373 testes sem verificá-la.

---

## 🗂 Estrutura do projeto

```text
├── Controllers/       Controllers de estoque, PDV, dashboard e usuários
├── Models/            Produtos, vendas e entidades comerciais
├── Security/          Policies de autorização e componentes de segurança
├── Data/              Contexto do Entity Framework Core
├── Migrations/        Migrations do EF Core
├── Services/           Regras de negócio, Kardex, auditoria e rentabilidade
├── Views/              Views Razor da aplicação
├── wwwroot/            CSS, JavaScript e recursos estáticos
├── tests.QA/           Suíte de testes automatizados
├── qa/                 Scripts e relatórios auxiliares de QA
└── .github/workflows/  Workflows de integração contínua
```

---

## 🔮 Próximos passos — Copiloto de IA

Uma evolução planejada é um **copiloto de IA para o Dono**, capaz de responder perguntas com base nos dados da própria loja, como:

- Quanto vendi nesta semana?
- Quais produtos estão há mais tempo sem movimentação?
- Qual forma de pagamento foi mais utilizada no período?
- Quais produtos apresentam maior diferença entre custo de aquisição e preço de venda?

A implementação deverá respeitar as políticas de autorização existentes e limitar o acesso às informações comerciais conforme o perfil do usuário. Essa funcionalidade é uma proposta de evolução, não uma capacidade já disponível na aplicação.

---

## ⚠️ Observações

- Os dados fiscais apresentados nos cupons de demonstração, como CNPJ, endereço e telefone, são fictícios e precisam ser parametrizados antes de qualquer utilização comercial.
- O banco de dados não é versionado pelo Git. Cada ambiente mantém sua própria base SQLite.
- As credenciais de demonstração devem ser verificadas e alteradas quando necessário, especialmente antes de disponibilizar o sistema publicamente.
- O uso em ambiente comercial exige validação das regras fiscais, operacionais, de segurança e de implantação aplicáveis ao estabelecimento.

---

<p align="center">Desenvolvido com C# e .NET 10 🚀</p>
