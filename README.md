# 🎛 Controle de Estoque e Caixa

Sistema web para gerenciamento de estoque e caixa de pequenas lojas, desenvolvido com **ASP.NET Core MVC** e **Entity Framework Core**.

## ­ƒîÉ Demo

Acesse o sistema em produ├º├úo: **[http://controle-estoque-loja.runasp.net](http://controle-estoque-loja.runasp.net)**

> ⚠️ O ambiente de demonstração é uma implantação anterior e **pode não refletir as últimas correções deste repositório** (branch `main`). Para o comportamento atual, rode o projeto localmente como descrito abaixo.
>
> O banco de dados é criado automaticamente na primeira execução (via migrations). Este repositório traz um banco de exemplo com dados fictícios — copie `controleestoque.exemplo.db` para `controleestoque.db` para começar rápido.

---

## ­ƒÜÇ Tecnologias

- .NET 10 / ASP.NET Core MVC
- Entity Framework Core 10 (Code-First)
- SQLite
- Razor Views com Runtime Compilation
- SweetAlert2

---

## 🚪 Rodando pela primeira vez

O repositório **não versiona o banco de dados real** (`controleestoque.db`): ele é ignorado
pelo Git, porque contém dados de operação e só faz sentido na máquina da loja.

Para começar rápido, copie o banco de exemplo:

```powershell
# dentro da pasta ControleEstoqueLoja
copy controleestoque.exemplo.db controleestoque.db
```

Depois é só subir a aplicação. O `dotnet run` sem argumento só funciona **dentro** da pasta
`ControleEstoqueLoja`; se o terminal estiver na pasta pai (é onde o VS Code abre o terminal),
passe `--project`:

```powershell
# na pasta pai do repositório:
dotnet run --project ControleEstoqueLoja

# ou dentro da pasta ControleEstoqueLoja:
cd ControleEstoqueLoja
dotnet run
```

Abra <http://localhost:5236> (Caixa) ou `/Home/Index` (Estoque).

Na primeira execução o sistema redireciona para `/Account/Setup`, onde você cria a conta de
**Dono** (nome, e-mail e senha). Essa tela só funciona enquanto o banco não tiver nenhum
usuário — depois ela redireciona para o login.

### Dados da empresa (pendente de parametrizar)

O nome do produto exibido no `<title>` e no rodapé é genérico de propósito
(**Controle de Estoque e Caixa**): o nicho ainda não foi decidido.

Já os **dados fiscais impressos no cupom ainda são fictícios e fixos no HTML** de
`Views/Caixa/GerarCupom.cshtml`:

| Dado | Valor atual (fictício) |
|---|---|
| CNPJ | `00.000.000/0001-00` |
| Endereço | `Rua Exemplo, 123 - Centro` |
| Telefone | `(11) 99999-9999` |

> ⚠️ **Estes valores precisam ser parametrizados antes de qualquer uso comercial.**
> Não versionar um CNPJ real aqui: ele ficaria visível no histórico do Git.
> O caminho é um registro de empresa no banco + uma tela de configuração, não um
> hard-coded no `.cshtml`.

> Se preferir começar do zero, basta **apagar** `controleestoque.db`: na próxima execução o
> `Program.cs` roda `Database.Migrate()` e recria todas as tabelas automaticamente.

### Banco de exemplo

O arquivo `controleestoque.exemplo.db` é uma base legada; `Program.cs` aplica as migrations
pendentes no primeiro start. Ele não fornece um catálogo inicial confiável, então cadastre os
produtos depois de criar a conta Dono. Para produtos **Por peso**, cadastre o preço diretamente
em **R$/kg**; a venda multiplica esse valor pela quantidade em kg, sem fator adicional.

---

## 🧰 Preparando uma máquina nova

Em vez de refazer tudo na mão, rode o **`setup.ps1`** uma única vez:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -ComDados
```

Ele faz tudo em um comando só:

1. **clone** do repositório (ou apenas ressincroniza, se a pasta já existir)
2. **checkout** da `main`
3. **copia** `controleestoque.exemplo.db` → `controleestoque.db`
4. **`dotnet tool restore`** (manifesto local, instala o `dotnet-ef`)
5. **`dotnet restore`** (pacotes NuGet)
6. **`dotnet build`**
7. **lista as migrations** e avisa se houver pendentes
8. **roda a suíte de testes**

### Qual usar em cada caso

| Situação | Comando |
|---|---|
| Máquina nova para desenvolver/testar | `.\setup.ps1 -ComDados` |
| Máquina da loja, com o banco real copiado do PC antigo | copie o `controleestoque.db` para a pasta e rode `.\setup.ps1` |
| Só sincronizar (dia a dia) | `git pull origin main` |
| Aplicar migrations, depois de ler o SQL delas | `.\setup.ps1 -AplicarMigrations` |

Parâmetros úteis: `-PularBuild`, `-PularTestes`, `-Destino` e `-Repositorio`.

> ⚠️ **O `-ComDados` só serve para testar.** Ele copia o banco de exemplo, que tem
> produtos e vendas fictícios. Na máquina da loja, copie o `controleestoque.db`
> real do PC antigo antes de rodar o script.

### O aviso das migrations pendentes

O script **nunca aplica migration sozinho**, mesmo quando encontra pendentes. Isso é
proposital, porque o `Program.cs` chama `Database.Migrate()` no start da aplicação — ou seja,
**`dotnet run` aplica as migrations pendentes sem perguntar nada**.

Se uma delas altera dados que já existem, o estrago é silencioso. Por isso o script
apenas mostra um alerta em destaque, lista quais são e deixa a decisão na mão:

```
   ATENCAO: HAI MIGRATIONS PENDENTES NESTE BANCO

  1 migration ainda NAO aplicada(s) neste controleestoque.db:

      20260930134556_DecimalParaCentavos

  O Program.cs chama Database.Migrate() no start da aplicacao. Portanto:

      dotnet run   ->   APLICA TUDO ISSO SOZINHO, SEM PERGUNTAR
```

Depois de ler o SQL em `Migrations/` e fazer o backup, aplique de propósito:

```powershell
Copy-Item controleestoque.db "$env:TEMP\backup-$(Get-Date -Format 'yyyyMMdd-HHmmss').db"
dotnet ef migrations list          # ver o que falta
.\setup.ps1 -AplicarMigrations      # aplicar de propósito
```

### Depois do setup

O dia a dia passa a ser só sincronizar:

```powershell
git pull origin main      # buscar
git add -A; git commit -m "..."; git push origin main   # enviar
```

O `controleestoque.db` é ignorado pelo Git, então ele **nunca entra no diff** e nunca causa
conflito de merge entre máquinas. É por isso que ele é local de cada máquina.

---

## ­ƒôª M├│dulo de Estoque

- Listagem de todos os produtos com busca por nome
- Cadastro de produto com os campos: nome, tipo, quantidade, pre├ºo e c├│digo de barras
- Edi├º├úo de produto existente
- Exclus├úo de produto
- Movimenta├º├úo de estoque (entrada e sa├¡da de quantidade)
- Produtos organizados por tipo: **Por peso**, **Prateleira**, **Geladeira** e **Freezer**

---

## ­ƒÆ░ M├│dulo de Caixa

### Realizando uma Venda
1. Acesse o **Caixa** (`Alt + C`)
2. Busque o produto pelo nome ou leia o c├│digo de barras
3. Adicione os itens ao carrinho
4. Informe o m├®todo de pagamento e o valor recebido
5. Finalize a venda ÔÇö o sistema calcula o troco automaticamente
6. O cupom fiscal ├® gerado para impress├úo

> **Cupom do vendedor é opcional:** a venda pode ser registrada sem identificar vendedor. Se a loja usa o programa de recompensa por venda, clique em **CUPOM DO VENDEDOR** (etapa do CPF) e informe o código — o vendedor precisa estar cadastrado e ativo para o cupom ser aceito.

### Cadastrando um Vendedor
1. Pressione `Alt + M` para abrir o Menu Administrativo
2. Selecione a op├º├úo **7 - Vendedores**
3. Clique em **Cadastrar Novo**
4. Preencha o nome e o c├│digo (cupom) do vendedor
5. Clique em **Salvar Registro**

---

## 🔐 Contas de acesso (Usuários)

Quem entra no sistema é um **`Usuario`** (ASP.NET Core Identity), com **dois papéis**:

| Papel | O que pode fazer |
|---|---|
| **Dono** | Tudo, inclusive financeiro, vendedores e a gestão de contas (`/Usuarios`) |
| **Operador** | PDV, suprimentos, reimpressão e consulta de vendedores — **sem** financeiro e **sem** criar/editar/excluir produtos |

- A tela `/Usuarios` é exclusiva do **Dono**. Um Operador que digitar a URL na mão recebe **403** —
  as permissões são aplicadas no servidor (`[Authorize(Policy = ...)]`), não só escondendo botões.
- Contas são criadas em `/Usuarios` → **+ NOVA CONTA**. O papel é escolhido no formulário e
  validado contra a lista conhecida antes de chegar ao Identity.
- O login é por **e-mail** (`UserName = Email`).

### ⚠️ Nomes de e-mail provisórios

O `controleestoque.db` **real** (ignorado pelo Git) tem hoje **uma única conta**, de teste, cujo
e-mail ainda carrega um **nome de nicho antigo** que não é mais o projeto. O endereço em si
**não é registrado aqui de propósito**, justamente para não voltar a vazar esse nome pelo
repositório.

| Conta | Papel | E-mail | Situação |
|---|---|---|---|
| `Leonardo (Dono)` | **Dono** | nome de nicho antigo (ver banco local) | **provisório** |

**Isso não é um bug e não afeta o funcionamento** — o e-mail é apenas o login. Ele só precisa
ser trocado quando o sistema for usado de verdade.

Motivos para ainda não ter sido trocado:

1. **Não existe tela de edição de usuário.** O `UsuariosController` só tem `Index` e `Criar` —
   não há `Editar`, nem reset de senha, nem desativar conta.
2. **`/Account/Setup` não serve de saída.** Ela só abre enquanto o banco não tiver nenhum
   usuário; com o Dono criado, redireciona para o login.
3. **A senha não é conhecida.** Ela só existe como hash no banco — quem define uma nova
   senha é a pessoa, não o código.

Quando for trocado, o caminho é criar a tela `/Usuarios/Editar` (nome + e-mail + nova senha),
alterando `UserName` e `Email` juntos via `UserManager` — o Identity exige os dois em sincronia.

> Nomes de e-mail **nunca** devem ser versionados: eles vivem só no banco local. Os e-mails
> usados pelos testes de QA (`dono@teste.com`, `operador@teste.com`) são criados em bancos
> descartáveis em `%TEMP%` e não tocam o banco real.

---

## ­ƒÅº Menu Administrativo (Alt + M)

| Tecla | Fun├º├úo |
|---|---|
| `1` | ­ƒÆ© Sangria ÔÇö retirada de dinheiro do caixa |
| `2` | ­ƒôÑ Suprimento ÔÇö entrada de dinheiro no caixa |
| `3` | ­ƒÅº Status do Caixa |
| `4` | ­ƒöÆ Fechamento do Caixa |
| `5` | ­ƒôè Relat├│rio do Caixa |
| `6` | ­ƒû¿´©Å Reimprimir ├║ltima venda |
| `7` | ­ƒæÑ Gerenciar Vendedores |

---

## Ôî¿´©Å Atalhos do Sistema

| Atalho | Fun├º├úo |
|---|---|
| `Alt + M` | Abre o Menu Administrativo |
| `Alt + C` | Navega para o Caixa |
| `Alt + H` | Navega para o Hist├│rico de Vendas |
| `Alt + E` | Navega para o Controle de Estoque |
| `ESC` | Abre/fecha o painel de ajuda com todos os atalhos |

---

## ­ƒôï Hist├│rico de Vendas

- Listagem de todas as vendas realizadas
- Exibe data, total, m├®todo de pagamento e vendedor
- Reimpress├úo de cupom de qualquer venda anterior

---

## ­ƒÅù´©Å Arquitetura

Padr├úo MVC com separa├º├úo em `Controllers`, `Models`, `Views` e `Data` (DbContext). Inje├º├úo de depend├¬ncia nativa do ASP.NET Core.
---

## 🧪 Desenvolvimento

```powershell
dotnet build                              # compilar (0 avisos, 0 erros)
dotnet run --project ControleEstoqueLoja  # subir a aplicação (http://localhost:5236)
cd tests.QA; dotnet test                  # suíte de testes de caracterização (78 testes)
```

### Migrations

O projeto usa um **manifesto local de ferramentas** (`dotnet-tools.json`) com o `dotnet-ef`,
então o comando é `dotnet ef` (sem prefixo `tool`):

```powershell
dotnet ef migrations add NomeDaMigration
dotnet ef database update
dotnet ef migrations list
```

> Nunca rode `dotnet ef` contra o `controleestoque.db` real sem backup — as migrations
> alteram o schema do banco.

### Suíte de testes

`tests.QA/` é uma suíte xUnit de **caracterização**: os testes descrevem o comportamento
esperado e ficam **vermelhos enquanto o bug existir**, virando verdes quando corrigido.
Cada teste cria um SQLite descartável em `%TEMP%` — o banco real nunca é tocado.

Os achados e a severidade de cada bug estão em [`qa/RELATORIO-QA.md`](qa/RELATORIO-QA.md)
e [`qa/falhas.csv`](qa/falhas.csv).

### Estrutura

```
ControleEstoqueLoja/
├── Controllers/          HomeController (estoque) e CaixaController (PDV)
├── Models/               Produto, Venda, VendaItem, Vendedor, CaixaMovimentacao...
├── Data/                 ApplicationDbContext (configura FKs e exclusão lógica)
├── Migrations/           migrations do EF Core
├── Views/                Razor
├── wwwroot/              Bootstrap, jQuery e admin-caixa.js
├── tests.QA/             suíte xUnit de caracterização
├── qa/                   relatório de QA, falhas.csv e script HTTP
├── controleestoque.exemplo.db   banco de exemplo (versionado)
└── controleestoque.db           banco real (ignorado pelo Git)
```

---
> Desenvolvido com C# e .NET 10 ­ƒöÀ

