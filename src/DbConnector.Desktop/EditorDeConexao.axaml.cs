using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DbConnector.Desktop;

/// <summary>Cadastro de uma conexão (nova ou existente). Fecha com a conexão salva, ou null se cancelou.</summary>
public partial class EditorDeConexao : Window
{
    private readonly IReadOnlyList<Conexao> _outras;

    // O designer do Avalonia precisa de um construtor sem parâmetros.
    public EditorDeConexao() : this(null, [])
    {
    }

    public EditorDeConexao(Conexao? atual, IReadOnlyList<Conexao> outras)
    {
        InitializeComponent();
        _outras = outras;
        Title = atual is null ? "Nova conexão" : $"Conexão {atual.Nome}";
        var c = atual ?? new Conexao("", "", "", "", PortaLocal: ArquivoDeConexoes.PortaLivre(outras));
        Nome.Text = c.Nome;
        Emissor.Text = c.Emissor;
        ClientId.Text = c.ClientId;
        Host.Text = c.Host;
        PortaGateway.Value = c.PortaGateway;
        PortaLocal.Value = c.PortaLocal;
        BancoPadrao.Text = c.BancoPadrao;
        SoLeitura.IsChecked = c.SoLeitura;
        IdpHint.Text = c.IdpHint;
        Opened += (_, _) => Nome.Focus();
    }

    private void Salvar(object? sender, RoutedEventArgs e)
    {
        static string? Opcional(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        var conexao = new Conexao(
            (Nome.Text ?? "").Trim(),
            (Emissor.Text ?? "").Trim().TrimEnd('/'),
            (ClientId.Text ?? "").Trim(),
            (Host.Text ?? "").Trim(),
            (int)(PortaGateway.Value ?? 0),
            (int)(PortaLocal.Value ?? 0),
            Opcional(BancoPadrao.Text),
            SoLeitura.IsChecked == true,
            Opcional(IdpHint.Text));
        var problemas = ArquivoDeConexoes.Validar([.. _outras, conexao]);
        if (problemas.Count > 0)
        {
            Erros.Text = string.Join("\n", problemas.Select(p => p.StartsWith(conexao.Nome + ": ", StringComparison.Ordinal) ? p[(conexao.Nome.Length + 2)..] : p));
            return;
        }
        Close(conexao);
    }

    private void Cancelar(object? sender, RoutedEventArgs e) => Close(null);
}
