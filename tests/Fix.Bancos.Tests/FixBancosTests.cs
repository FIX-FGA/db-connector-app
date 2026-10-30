using System.Net.Sockets;
using Fix.Bancos;
using Fix.Bancos.Protocol;

namespace Fix.Bancos.Tests;

public class FixBancosTests
{
    [Fact]
    public void Sem_argumentos_o_cli_usa_dev_e_o_aplicativo_nao_liga_nenhum()
    {
        Assert.Equal(["dev"], Opcoes.Ler([])!.Ambientes.Select(a => a.Nome));
        Assert.Empty(Opcoes.Ler([], padrao: [])!.Ambientes);
        Assert.Equal(["dev", "prod"], Opcoes.Ler(["dev", "prod", "dev"])!.Ambientes.Select(a => a.Nome));
        Assert.Null(Opcoes.Ler(["--ajuda"]));
        Assert.Throws<ArgumentException>(() => Opcoes.Ler(["homolog"]));
    }

    [Fact]
    public void Prod_e_so_leitura_e_a_url_do_dbeaver_vai_sem_ssl()
    {
        Assert.True(Ambiente.Conhecidos["prod"].SoLeitura);
        Assert.False(Ambiente.Conhecidos["dev"].SoLeitura);
        Assert.Equal("jdbc:postgresql://localhost:15433/fix_core?sslmode=disable", Ambiente.Conhecidos["qa"].UrlJdbc);
    }

    [Fact]
    public async Task Senha_local_aceita_sem_hifens_e_em_minusculas()
    {
        var senha = new SenhaLocal();
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", senha.Valor);
        Assert.True(await senha.ConfereAsync(senha.Valor.Replace("-", "").ToLowerInvariant(), CancellationToken.None));
        Assert.False(await new SenhaLocal("AAAA-BBBB-CCCC").ConfereAsync("AAAA-BBBB-CCCD", CancellationToken.None));
    }

    [Fact]
    public async Task Senha_local_errada_e_recusada_antes_do_login_e_a_porta_fica_ocupada()
    {
        var ambiente = new Ambiente("teste", "gateway.invalido", 5432, "bancos-teste", 25432);
        var opcoes = new Opcoes([ambiente], Ambiente.Emissor, null, false, 25431);
        using var http = new HttpClient();
        await using var conector = new Conector(opcoes, http, new SenhaLocal("AAAA-BBBB-CCCC"));
        var registros = new List<string>();
        conector.Registro += (_, mensagem) => { lock (registros) registros.Add(mensagem); };
        conector.Ligar(ambiente, entrarAgora: false);

        // Outro FIX bancos na mesma porta não abre.
        await using (var outro = new Conector(opcoes, http, new SenhaLocal()))
            Assert.Throws<InvalidOperationException>(() => outro.Ligar(ambiente, entrarAgora: false));

        using var dbeaver = new TcpClient();
        await dbeaver.ConnectAsync("127.0.0.1", 25432);
        var stream = dbeaver.GetStream();
        await stream.WriteAsync(PgWire.Startup(PgWire.ProtocolVersion3, PgWire.StartupParameters([new("user", "eu"), new("database", "fix_core")])));
        var pedido = await PgWire.ReadAsync(stream, CancellationToken.None);
        Assert.Equal((byte)'R', pedido!.Value.Type);
        await PgWire.WriteAsync(stream, new PgMessage((byte)'p', [.. "errada"u8.ToArray(), 0]), CancellationToken.None);
        var erro = await PgWire.ReadAsync(stream, CancellationToken.None);

        Assert.Equal((byte)'E', erro!.Value.Type);
        Assert.Equal("28P01", PgWire.ParseErrorFields(erro.Value.Body)['C']);
        lock (registros) Assert.Contains(registros, r => r.Contains("senha local errada"));
    }
}
