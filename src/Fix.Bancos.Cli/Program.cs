using Fix.Bancos;

const string Ajuda = """
    fix-bancos-cli: acesso aos bancos do FIX pelo DBeaver com o login do Keycloak (no navegador), sem janela.
    Com área de trabalho, prefira o aplicativo FIX bancos (fix-bancos).

    Uso: fix-bancos-cli [dev] [qa] [prod]            (sem nada: dev)

    Para cada ambiente, abre o navegador no login e fica escutando em localhost:
      dev  → localhost:15432     qa → localhost:15433     prod → localhost:15434
    No DBeaver: PostgreSQL, host localhost, a porta do ambiente, database fix_core, usuário qualquer,
    a SENHA LOCAL que esta janela mostra (muda a cada vez que abre) e SEM SSL (o fix-bancos cifra até o
    gateway). Deixe esta janela aberta enquanto usa.

    Opções:
      --idp <alias>        vai direto ao provedor (ex.: microsoft), sem a tela do Keycloak
      --emissor <url>      outro realm (padrão: https://sso.fix.webpassos.com.br/realms/internal-trusted)
      --gateway <host:porta> --client <id> --porta <n>   ambiente fora da lista (um só)
      --inseguro           aceita certificado inválido do gateway (só para teste local)
      --senha-local <s>    usa esta senha local em vez de gerar uma (só para teste)
    """;

Opcoes? opcoes;
try
{
    opcoes = Opcoes.Ler(args);
}
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Ajuda);
    return 2;
}
if (opcoes is null)
{
    Console.WriteLine(Ajuda);
    return 0;
}

using var parar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; parar.Cancel(); };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
await using var conector = new Conector(opcoes, http, new SenhaLocal(opcoes.SenhaLocal));
conector.Registro += (ambiente, mensagem) => Console.WriteLine($"[{ambiente.Nome}] {mensagem}");

Console.WriteLine("fix-bancos-cli: acesso aos bancos do FIX pelo DBeaver (Ctrl+C encerra).");
Console.WriteLine();
Console.WriteLine($"  Senha local desta sessão (no DBeaver, campo senha): {conector.SenhaLocal.Valor}");
Console.WriteLine("  Ela muda a cada vez que o fix-bancos abre; não a compartilhe.");
Console.WriteLine();
foreach (var ambiente in opcoes.Ambientes)
{
    try
    {
        conector.Ligar(ambiente);
    }
    catch (InvalidOperationException e)
    {
        Console.Error.WriteLine($"[{ambiente.Nome}] {e.Message}");
        return 1;
    }
    // Um login por vez no navegador, na ordem dos ambientes.
    await conector.EntrarAsync(ambiente);
}

try
{
    await Task.Delay(Timeout.Infinite, parar.Token);
}
catch (OperationCanceledException)
{
}
return 0;
