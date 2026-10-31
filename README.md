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

O aplicativo não tem nada de uma organização: você **cadastra as conexões** nele (emissor OIDC, client, gateway) ou
**importa** o arquivo que alguém exportou. Quem decide o acesso é o servidor: um gateway PostgreSQL que confere o token
(assinatura, emissor, audiência, grupos) antes de conectar no banco (ex.: o db-gateway da FIX).

## Instalar

Baixe da [última release](../../releases/latest) o do seu sistema e confira o `SHA256SUMS`:

| Sistema | Arquivo |
| --- | --- |
| Windows 10/11 | `db-connector-windows-x64.msi`: instalador (assinado), por usuário, sem pedir administrador; cria os atalhos **DB Connector** no Menu Iniciar e na Área de Trabalho |
| Linux | `db-connector-linux-x64.tar.gz`: extraia; `db-connector` (janela) e `db-connector-cli` |
| macOS (Apple Silicon) | `db-connector-macos-arm64.tar.gz`: extraia; na primeira vez, botão direito → Abrir |

O `db-connector-windows-x64.exe` da release é o mesmo programa, sem instalador.

## Desinstalar

Windows: Configurações → Aplicativos → **DB Connector** → Desinstalar. As conexões cadastradas ficam em
`%APPDATA%\DB Connector\conexoes.json` (apague a pasta para tirá-las também). Linux e macOS: apague a pasta extraída
e `~/.config/DB Connector` (Linux) ou `~/Library/Application Support/DB Connector` (macOS).

## Usar

1. Abra o **DB Connector**. A janela mostra a **senha local** desta sessão e as conexões cadastradas.
2. Na primeira vez, cadastre a conexão: **Nova conexão** (nome, emissor, client, gateway, porta local) ou **Importar**
   o arquivo `.json` que alguém exportou. Ela fica salva no computador.
3. Clique em **Conectar**: o navegador abre no login. Depois do login, o cartão fica verde com o seu e-mail.
4. No aplicativo SQL: conexão PostgreSQL (ou **Copiar URL** e colar em Nova conexão → URL):

   | Campo | Valor |
   | --- | --- |
   | Host e porta | `localhost` e a porta local da conexão |
   | Database | o database padrão da conexão; no DBeaver, marque **Show all databases** |
   | Usuário | qualquer um (quem vale é o login no navegador) |
   | Senha | a senha local da janela (muda a cada vez que o aplicativo abre) |
   | SSL | desligado (o trecho local não sai da máquina; o aplicativo cifra até o gateway) |

5. Deixe a janela aberta enquanto usa. Fechar encerra as conexões.

**Exportar** salva as conexões num `.json` para passar a outra pessoa: só endereços e ids de clients públicos, sem
senha nem token. Quando a sessão do client OIDC acaba, o navegador abre de novo na próxima conexão (com a sessão do
provedor ainda aberta no navegador, é imediato).

Linha de comando (Linux sem área de trabalho, scripts): `db-connector-cli [conexão...]` usa as conexões cadastradas
(ou `--arquivo <conexoes.json>`), com a senha local impressa no terminal. `db-connector-cli --ajuda` lista as opções.

## Conexões

O arquivo ([`conexoes.exemplo.json`](conexoes.exemplo.json)) é o mesmo que o aplicativo salva, exporta e importa:

| Campo | O que é |
| --- | --- |
| `nome` | Como a conexão aparece (ex.: "ACME dev") |
| `emissor` | URL do emissor OIDC (no Keycloak, a do realm) |
| `clientId` | Client OIDC público, com PKCE `S256` e redirect `http://127.0.0.1:15431/callback` |
| `host`, `porta` | O gateway PostgreSQL (TLS) |
| `portaLocal` | Onde o aplicativo SQL conecta (`localhost`), uma por conexão; a `15431` é a do retorno do login |
| `bancoPadrao` | Database que o aplicativo SQL abre primeiro (opcional) |
| `somenteLeitura` | Só o rótulo na janela; quem garante é o banco (opcional) |
| `idpHint` | Provedor de identidade direto (`kc_idp_hint` do Keycloak, ex.: `microsoft`; opcional) |

**No provedor OIDC (Keycloak):** um client público por ambiente, com *Standard flow* e PKCE `S256`, redirect
`http://127.0.0.1:15431/callback` e um mapper de audiência para o que o gateway confere.

**No gateway:** recebe a conexão PostgreSQL com TLS, pede senha em texto (`AuthenticationCleartextPassword`, protegida
pelo TLS) e recebe o access token como senha; o resto é o protocolo do PostgreSQL sem mudança.

## Desenvolvimento

```bash
dotnet build DbConnector.slnx
dotnet test DbConnector.slnx
dotnet run --project src/DbConnector.Desktop
dotnet run --project src/DbConnector.Desktop -- --arquivo conexoes.exemplo.json
dotnet run --project src/DbConnector.Desktop -- --gateway localhost:6432 --client teste --emissor http://localhost:8080/realms/teste --inseguro
dotnet publish src/DbConnector.Desktop -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o out
pwsh instalador/montar.ps1 -Exe out/db-connector.exe -Versao 1.0.0      # MSI (WiX 5)
```

| Projeto | O que é |
| --- | --- |
| `src/DbConnector.Core` | Núcleo: conexões (salvar, importar, exportar), login no navegador (PKCE), senha local, porta local, protocolo do PostgreSQL |
| `src/DbConnector.Desktop` | A janela ([Avalonia](https://avaloniaui.net/)) |
| `src/DbConnector.Cli` | A linha de comando |
| `instalador/` | Instalador MSI ([WiX](https://wixtoolset.org/) 5) e o script que o monta |

Branches: `dev`, `qa` e `master`. O CI compila e testa todo push e PR; na `master`, publica os executáveis e o
instalador numa release.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Only the Windows executable (`db-connector-windows-x64.exe`) and the Windows installer that packages it
(`db-connector-windows-x64.msi`), built by this repository's GitHub Actions workflow
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) from the `master` branch, are signed. Every signing request
is approved manually.

Team roles:

- **Committers and reviewers:** members of the [FIX-FGA developers team](https://github.com/orgs/FIX-FGA/teams/developers).
  Changes reach `master` only through pull requests, reviewed by a team member.
- **Approvers:** the [FIX-FGA organization owners](https://github.com/orgs/FIX-FGA/people?query=role%3Aowner)
  ([@sfourm](https://github.com/sfourm), [@yuridsfga](https://github.com/yuridsfga),
  [@cristian272001](https://github.com/cristian272001)).

Multi-factor authentication is required for every team member, on GitHub and on SignPath.

**Privacy policy:** this program will not transfer any information to other networked systems unless specifically
requested by the user. It only talks to the systems the user chooses to connect to: the OpenID Connect identity
provider and the database gateway of the connections the user registers in the program (login in the user's
browser). The only thing it stores on disk is the list of connections the user registered (addresses and client ids,
no credentials); login tokens are kept in memory only while the program is open.

## Assinatura (para quem mantém)

O executável e o instalador do Windows são assinados pela SignPath Foundation, de graça para projetos open source, para
o Windows (Controle Inteligente de Aplicativos e SmartScreen) abri-los sem bloqueio. O CI assina o `.exe`, monta o MSI
com ele e assina o MSI. Configuração, uma vez:

1. Inscrição do projeto em [signpath.org](https://signpath.org/) (formulário de projetos open source).
2. Na SignPath, projeto `db-connector-app` com o repositório conectado como *trusted build system* (GitHub), a política de
   assinatura `release-signing` e duas configurações de artefato, `windows` e `instalador`:
   ```xml
   <!-- windows -->
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="db-connector-windows-x64.exe">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   <!-- instalador -->
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <msi-file path="db-connector-windows-x64.msi">
         <authenticode-sign />
       </msi-file>
     </zip-file>
   </artifact-configuration>
   ```
3. No GitHub: secret `SIGNPATH_API_TOKEN` (usuário de CI da SignPath com permissão de enviar pedidos) e variável
   `SIGNPATH_ORGANIZATION_ID`.

Sem o secret, o CI publica a release sem assinatura e avisa no log. Conferir no Windows:
`Get-AuthenticodeSignature db-connector-windows-x64.msi` (Status `Valid`).

## Licença

[MIT](LICENSE).
