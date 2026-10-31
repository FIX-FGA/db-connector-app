using System.Net.Security;
using System.Net.Sockets;
using DbConnector.Protocol;

namespace DbConnector;

/// <summary>Token de um ambiente: reaproveita, renova pelo refresh token ou, se a sessão acabou, abre o navegador.</summary>
public sealed class TokenDaConexao(Conexao ambiente, LoginNavegador login)
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private Tokens? _tokens;

    /// <summary>O navegador vai abrir (a sessão acabou ou ainda não houve login).</summary>
    public event Action? LoginIniciado;

    /// <summary>Token novo, do navegador ou renovado.</summary>
    public event Action<Tokens>? Obtido;

    public async Task<Tokens> ObterAsync(CancellationToken ct)
    {
        await _trava.WaitAsync(ct);
        try
        {
            if (_tokens is { } atual && atual.ExpiraEm - DateTimeOffset.UtcNow > TimeSpan.FromSeconds(60)) return atual;
            if (_tokens?.RefreshToken is { } refresh && await login.RenovarAsync(ambiente, refresh, ct) is { } renovado)
            {
                _tokens = renovado;
            }
            else
            {
                LoginIniciado?.Invoke();
                _tokens = await login.EntrarAsync(ambiente, ct);
            }
            Obtido?.Invoke(_tokens);
            return _tokens;
        }
        finally
        {
            _trava.Release();
        }
    }
}

/// <summary>
/// Uma conexão do DBeaver em localhost: responde "sem SSL" (o trecho local não sai da máquina), pede a senha local (a da
/// janela do aplicativo), abre TLS até o gateway, repassa o StartupMessage e responde o pedido de senha do gateway com o
/// access token. Daí em diante, repassa tudo.
/// </summary>
public sealed class Sessao(Conexao ambiente, TokenDaConexao token, Opcoes opcoes, SenhaLocal senhaLocal, Registrar registrar)
{
    public async Task AtenderAsync(TcpClient local, CancellationToken ct)
    {
        using var _ = local;
        local.NoDelay = true;
        var stream = local.GetStream();
        while (true)
        {
            var inicio = await PgWire.ReadStartupAsync(stream, ct);
            if (inicio is null) return;
            var (codigo, corpo) = inicio.Value;
            if (codigo is PgWire.SslRequestCode or PgWire.GssEncRequestCode)
            {
                await stream.WriteAsync("N"u8.ToArray(), ct);
                continue;
            }
            if (codigo == PgWire.CancelRequestCode)
            {
                await using var cancelamento = await ConectarGatewayAsync(ct);
                await cancelamento.Stream.WriteAsync(PgWire.Startup(codigo, corpo), ct);
                await cancelamento.Stream.FlushAsync(ct);
                return;
            }

            // Senha local antes de tudo: quem não a tem não dispara nem o login no navegador.
            await PgWire.WriteAsync(stream, PgWire.Authentication(3), ct);
            var resposta = await PgWire.ReadAsync(stream, ct);
            if (resposta is not { Type: (byte)'p' } senha) return;
            if (!await senhaLocal.ConfereAsync(new PgReader(senha.Body).CString(), ct))
            {
                registrar(ambiente, "conexão local recusada: senha local errada");
                await PgWire.WriteAsync(stream, PgWire.Error("28P01", $"senha local errada: use a que aparece na janela do {Opcoes.NomeDoAplicativo}"), ct);
                return;
            }

            Tokens tokens;
            try
            {
                tokens = await token.ObterAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                registrar(ambiente, $"login não concluído: {e.Message}");
                await PgWire.WriteAsync(stream, PgWire.Error("28000", $"login no navegador não concluído ({e.Message}); veja a janela do {Opcoes.NomeDoAplicativo}"), ct);
                return;
            }

            await using var gateway = await ConectarGatewayAsync(ct);
            await gateway.Stream.WriteAsync(PgWire.Startup(codigo, corpo), ct);
            await gateway.Stream.FlushAsync(ct);

            var pedido = await PgWire.ReadAsync(gateway.Stream, ct);
            if (pedido is not { } mensagem) return;
            if (mensagem.Type == 'R' && new PgReader(mensagem.Body).Int32() == 3)
                await PgWire.WriteAsync(gateway.Stream, new PgMessage((byte)'p', [.. System.Text.Encoding.UTF8.GetBytes(tokens.AccessToken), 0]), ct);
            else
                await PgWire.WriteAsync(stream, mensagem, ct); // erro do gateway (ex.: sem acesso): vai para o DBeaver

            using var ambos = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var ida = Copiar(stream, gateway.Stream, ambos.Token);
            var volta = Copiar(gateway.Stream, stream, ambos.Token);
            await Task.WhenAny(ida, volta);
            await ambos.CancelAsync();
            try { await Task.WhenAll(ida, volta); } catch { /* um lado fechou */ }
            return;
        }
    }

    private static async Task Copiar(Stream origem, Stream destino, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        int lidos;
        while ((lidos = await origem.ReadAsync(buffer, ct)) > 0)
        {
            await destino.WriteAsync(buffer.AsMemory(0, lidos), ct);
            await destino.FlushAsync(ct);
        }
    }

    private sealed record Conexao(TcpClient Tcp, Stream Stream) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            Tcp.Dispose();
        }
    }

    private async Task<Conexao> ConectarGatewayAsync(CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(ambiente.Host, ambiente.PortaGateway, ct);
        Stream stream = tcp.GetStream();
        await stream.WriteAsync(PgWire.Startup(PgWire.SslRequestCode, []), ct);
        var resposta = new byte[1];
        if (!await PgWire.ReadExactAsync(stream, resposta, ct) || resposta[0] != 'S') throw new IOException("o gateway não aceitou TLS");
        var tls = new SslStream(stream, leaveInnerStreamOpen: false,
            opcoes.AceitarCertificadoInvalido ? (_, _, _, _) => true : null);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = ambiente.Host }, ct);
        return new Conexao(tcp, tls);
    }
}
