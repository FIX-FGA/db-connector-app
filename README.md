# DB Connector

Aplicativo de desktop (Windows, Linux e macOS) que funciona como **proxy local de SSO** para bancos PostgreSQL: o
DBeaver (ou DataGrip, pgAdmin, psql...) conecta em `localhost`, o login é feito **no navegador** pelo provedor OIDC da
organização (Keycloak, com ou sem um provedor externo como o Microsoft Entra ID) e ninguém precisa de senha de banco
nem de port-forward.

```
Aplicativo SQL ──localhost:15432──▶ DB Connector ──TLS (access token como senha)──▶ gateway PostgreSQL ──▶ banco
 (senha local)                       │ login no navegador (authorization code + PKCE)
                                     └─ renova o token sozinho; o token fica só na memória
```

O aplicativo não guarda segredo nenhum e não tem nada de uma organização no código: **quem distribui** define o nome,
o emissor OIDC e os ambientes numa [configuração](#configuração), que o pipeline embute no executável. Quem decide o
acesso é o servidor: um gateway PostgreSQL que confere o token (assinatura, emissor, audiência, grupos) antes de
conectar no banco (ex.: o db-gateway da FIX).

## Instalar

Baixe da [última release](../../releases/latest) o do seu sistema e confira o `SHA256SUMS`:

| Sistema | Arquivo |
| --- | --- |
| Windows 10/11 | `db-connector-windows-x64.exe` (assinado; não precisa instalar, é só abrir) |
| Linux | `db-connector-linux-x64` (`chmod +x`) · sem área de trabalho: `db-connector-cli-linux-x64` |
| macOS (Apple Silicon) | `db-connector-macos-arm64` (`chmod +x`; na primeira vez, botão direito → Abrir) |

As releases deste repositório saem com a configuração da FIX-FGA (variável do pipeline). Para outra organização, use
`--config` ou gere o seu build (veja [Configuração](#configuração)).

## Desinstalar

O DB Connector não tem instalador nem altera nada no sistema: para remover, feche a janela e apague o executável (e o
`config.json` ao lado dele e o atalho, se você criou). Não há dados guardados em disco.

## Usar

1. Abra o aplicativo. A janela mostra a **senha local** desta sessão e os ambientes da configuração.
2. Clique em **Conectar** no ambiente: o navegador abre no login. Depois do login, o cartão fica verde com o seu e-mail.
3. No aplicativo SQL: conexão PostgreSQL (ou **Copiar URL** e colar em Nova conexão → URL):

   | Campo | Valor |
   | --- | --- |
   | Host e porta | `localhost` e a porta local do ambiente (a janela mostra) |
   | Database | o `bancoPadrao` da configuração; no DBeaver, marque **Show all databases** |
   | Usuário | qualquer um (quem vale é o login no navegador) |
   | Senha | a senha local da janela (muda a cada vez que o aplicativo abre) |
   | SSL | desligado (o trecho local não sai da máquina; o aplicativo cifra até o gateway) |

4. Deixe a janela aberta enquanto usa. Fechar encerra as conexões.

Quando a sessão do client OIDC acaba, o navegador abre de novo na próxima conexão (com a sessão do provedor ainda aberta
no navegador, é imediato).

Linha de comando (Linux sem área de trabalho, scripts): `db-connector-cli [ambiente...]`, com as mesmas portas e a
senha local impressa no terminal. `db-connector-cli --ajuda` lista as opções.

## Configuração

Um JSON ([`config.exemplo.json`](config.exemplo.json)), lido nesta ordem: `--config <arquivo>`, `config.json` ao lado do
executável, o `config.json` embutido no build. Sem nenhum, o aplicativo abre sem ambientes.

| Campo | O que é |
| --- | --- |
| `nome`, `descricao` | O que a janela mostra (ex.: "FIX bancos") |
| `emissor` | URL do emissor OIDC (no Keycloak, a do realm) |
| `idpHint` | Opcional: provedor de identidade direto (`kc_idp_hint` do Keycloak, ex.: `microsoft`) |
| `portaRetorno` | Porta do retorno do login: `http://127.0.0.1:<portaRetorno>/callback` (padrão `15431`) |
| `bancoPadrao` | Database que o aplicativo SQL abre primeiro |
| `ambientes[]` | `nome`, `host` e `porta` do gateway, `clientId` (client OIDC público, PKCE), `portaLocal`, `somenteLeitura` (só o rótulo; quem garante é o banco) e `bancoPadrao` próprio |

**No pipeline:** o CI lê a variável `DB_CONNECTOR_CONFIG` (Settings → Secrets and variables → Actions → **Variables**),
confere o JSON e o embute nos executáveis. Não é segredo (são endereços e ids de clients públicos), mas vai dentro do
binário e aparece para quem o usa.

**No provedor OIDC (Keycloak):** um client público por ambiente, com *Standard flow* e PKCE `S256`, redirect
`http://127.0.0.1:<portaRetorno>/callback` e um mapper de audiência para o que o gateway confere.

**No gateway:** recebe a conexão PostgreSQL com TLS, pede senha em texto (`AuthenticationCleartextPassword`, protegida
pelo TLS) e recebe o access token como senha; o resto é o protocolo do PostgreSQL sem mudança.

## Desenvolvimento

```bash
dotnet build DbConnector.slnx
dotnet test DbConnector.slnx
dotnet run --project src/DbConnector.Desktop -- --config config.exemplo.json
dotnet run --project src/DbConnector.Desktop -- --gateway localhost:6432 --client teste --emissor http://localhost:8080/realms/teste --inseguro
dotnet publish src/DbConnector.Desktop -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

| Projeto | O que é |
| --- | --- |
| `src/DbConnector.Core` | Núcleo: configuração, login no navegador (PKCE), senha local, porta local, protocolo do PostgreSQL |
| `src/DbConnector.Desktop` | A janela ([Avalonia](https://avaloniaui.net/)) |
| `src/DbConnector.Cli` | A linha de comando |

Branches: `dev`, `qa` e `master`. O CI compila e testa todo push e PR; na `master`, publica os executáveis numa release.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Only the Windows executable (`db-connector-windows-x64.exe`) built by this repository's GitHub Actions workflow
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) from the `master` branch is signed. Every signing request is
approved manually.

Team roles:

- **Committers and reviewers:** members of the [FIX-FGA developers team](https://github.com/orgs/FIX-FGA/teams/developers).
  Changes reach `master` only through pull requests, reviewed by a team member.
- **Approvers:** the [FIX-FGA organization owners](https://github.com/orgs/FIX-FGA/people?query=role%3Aowner)
  ([@sfourm](https://github.com/sfourm), [@yuridsfga](https://github.com/yuridsfga),
  [@cristian272001](https://github.com/cristian272001)).

Multi-factor authentication is required for every team member, on GitHub and on SignPath.

**Privacy policy:** this program will not transfer any information to other networked systems unless specifically
requested by the user. It only talks to the systems the user chooses to connect to: the OpenID Connect identity
provider set in its configuration (login in the user's browser) and the database gateway of the environment the user
selects. It stores nothing on disk; login tokens are kept in memory only while the program is open.

## Assinatura (para quem mantém)

O executável do Windows é assinado pela SignPath Foundation, de graça para projetos open source, para o Windows
(Controle Inteligente de Aplicativos e SmartScreen) abri-lo sem bloqueio. Configuração, uma vez:

1. Inscrição do projeto em [signpath.org](https://signpath.org/) (formulário de projetos open source).
2. Na SignPath, projeto `db-connector-app` com o repositório conectado como *trusted build system* (GitHub), a política de
   assinatura `release-signing` e a configuração de artefato `windows`:
   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="db-connector-windows-x64.exe">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   ```
3. No GitHub: secret `SIGNPATH_API_TOKEN` (usuário de CI da SignPath com permissão de enviar pedidos) e variável
   `SIGNPATH_ORGANIZATION_ID`.

Sem o secret, o CI publica a release sem assinatura e avisa no log. Conferir no Windows:
`Get-AuthenticodeSignature db-connector-windows-x64.exe` (Status `Valid`).

## Licença

[MIT](LICENSE).
