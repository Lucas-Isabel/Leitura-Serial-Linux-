using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

[ApiController]
[Route("api/[controller]")]
public class PrintController : ControllerBase
{
    [HttpPost]
    public IActionResult PrintReceipt([FromBody] object jsonData)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Deserializar o JSON recebido para um objeto dinâmico
        dynamic data = JsonConvert.DeserializeObject(jsonData.ToString());

        // Comando ESC/POS para o cabeçalho
        string header =
            "\x1B\x40" +                              // Inicializa a impressora
            "\x1B\x74\x10" +                           // Configura para caracteres Latinos
            "\x1B\x61\x01" +                          // Centraliza o texto
            "\x1B\x21\x08" +                          // Negrito
            $"{data.nome_comercio}\n\n" +             // Nome do comércio
            "\x1B\x61\x00" +                          // Alinha à esquerda
            "\x1B\x21\x00" +                          // Texto normal
            $"VENDEDOR: {data.vendedor}\n" +
            $"TICKET N°: {data.ticket.numero}\n" +
            $"HORA: {data.ticket.hora} BALANÇA {data.ticket.balanca}\n" +
            "============================================\n" +
            "NOME DO ARTIGO\n\tKG(Uni)\tR$/kg(Uni)\tR$\n" +
            "============================================\n";

        // Comando ESC/POS para os itens
        StringBuilder items = new StringBuilder();
        StringBuilder qr = new StringBuilder();
        foreach (var item in data.itens)
        {
            string input = item.preco_por_unidade; // Exemplo: pode ser "2,5" ou "2,0"

            // Converte a string para um número decimal
            decimal numeroDecimal = decimal.Parse(input);

            // Multiplica o número decimal por 100 para deslocar as casas decimais
            int numeroInteiro = (int)(numeroDecimal * 100);

            // Formata o número como uma string com 4 dígitos
            string numeroFormatado = numeroInteiro.ToString("D7");

            string eanbase = $"20{item.codigo_produto}{numeroFormatado}";

            string ean = eanbase + CalcularDVEAN13(eanbase);

            items.AppendLine($"{item.nome_artigo}\n\t{item.quantidade}\t{item.preco_por_unidade}\t\t{item.preco_total}\n");
            qr.Append($"{ean}\n");
        }

        // Comando ESC/POS para o resumo e pagamento
        string footer =
            "============================================\n" +
            $"ARTIGOS: {data.resumo.total_artigos}\t\tTOTAL: {data.resumo.total_a_pagar}\n" +
            "============================================\n" +
            $"{data.pagamento.metodo}\t\tR$ {data.pagamento.valor_pago}\n\n";

        // Gerar QR Code
        string codigoQr = qr.ToString();  // Você pode ajustar o conteúdo
        byte[] escPosQRCode = GerarQRCode(codigoQr);

        // Comando ESC/POS para a mensagem final
        string finalMessage =
            "\x1B\x61\x01" +                          // Centraliza o texto
            $"\n\n{data.qrcode.mensagem_comercio}\n\n" +    // Mensagem do comércio
            "\x1D\x56\x41\x10";                       // Corte de papel parcial


        // Converter para bytes usando a codificação Latin-1 (ISO 8859-1)
        var encoder = new System.Text.ASCIIEncoding();
        byte[] escPosHeader = Encoding.GetEncoding("ISO-8859-1").GetBytes(header);
        byte[] escPosItems = Encoding.GetEncoding("ISO-8859-1").GetBytes(items.ToString());
        byte[] escPosFooter = Encoding.GetEncoding("ISO-8859-1").GetBytes(footer);
        byte[] escPosFinalMessage = Encoding.GetEncoding("ISO-8859-1").GetBytes(finalMessage);

        // Concatenar todos os comandos
        byte[] allCommands = new byte[
            escPosHeader.Length + escPosItems.Length + escPosFooter.Length +
            escPosQRCode.Length + escPosFinalMessage.Length];

        Buffer.BlockCopy(escPosHeader, 0, allCommands, 0, escPosHeader.Length);
        Buffer.BlockCopy(escPosItems, 0, allCommands, escPosHeader.Length, escPosItems.Length);
        Buffer.BlockCopy(escPosFooter, 0, allCommands, escPosHeader.Length + escPosItems.Length, escPosFooter.Length);
        Buffer.BlockCopy(escPosQRCode, 0, allCommands, escPosHeader.Length + escPosItems.Length + escPosFooter.Length, escPosQRCode.Length);
        Buffer.BlockCopy(escPosFinalMessage, 0, allCommands, escPosHeader.Length + escPosItems.Length + escPosFooter.Length + escPosQRCode.Length, escPosFinalMessage.Length);

        try
        {
            // Enviar os comandos ESC/POS diretamente para a impressora
            string printerName = "POS-80"; // Substitua pelo nome da sua impressora
            string tempFilePath = Path.Combine("/tmp", "printfile.bin");

            // Gravar comandos em um arquivo temporário
            System.IO.File.WriteAllBytes(tempFilePath, allCommands);

            // Chamar o comando lp para imprimir
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lp",
                    Arguments = $"-d {printerName} {tempFilePath}",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            // Verificar se o comando foi executado com sucesso
            if (process.ExitCode != 0)
            {
                throw new Exception($"Erro ao enviar para a impressora: {process.StandardError.ReadToEnd()}");
            }

            // Deletar o arquivo temporário após a impressão
            System.IO.File.Delete(tempFilePath);

            return Ok(new { message = "Recibo impresso com sucesso!" });
        }
        catch (Exception ex)
        {
            // Captura de erro e listagem de impressoras disponíveis
            var printers = ListPrinters();
            return StatusCode(500, new { error = ex.Message, printers });
        }
    }

    private List<string> ListPrinters()
    {
        var printers = new List<string>();
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lpstat",
                    Arguments = "-p",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd();
                printers.AddRange(output.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
            }
            else
            {
                var error = process.StandardError.ReadToEnd();
                printers.Add($"Erro ao listar impressoras: {error}");
            }
        }
        catch (Exception ex)
        {
            printers.Add($"Erro ao listar impressoras: {ex.Message}");
        }

        return printers;
    }

    static int CalcularDVEAN13(string eanBase)
    {
        int somaImpares = 0;
        int somaPares = 0;

        for (int i = 0; i < eanBase.Length; i++)
        {
            int digito = int.Parse(eanBase[i].ToString());
            if (i % 2 == 0)
            {
                somaImpares += digito;
            }
            else
            {
                somaPares += digito;
            }
        }

        int somaTotal = somaImpares + (somaPares * 3);
        int dv = (10 - (somaTotal % 10)) % 10;

        return dv;
    }

    static byte[] GerarQRCode(string qrData)
    {
        int storeLen = qrData.Length + 3;
        byte storePL = (byte)(storeLen % 256);
        byte storePH = (byte)(storeLen / 256);

        byte[] modelQR = { 0x1d, 0x28, 0x6b, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 };
        byte[] sizeQR = { 0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x43, 0x06 };
        byte[] errorQR = { 0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x45, 0x31 };
        byte[] storeQR = { 0x1d, 0x28, 0x6b, storePL, storePH, 0x31, 0x50, 0x30 };
        byte[] printQR = { 0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x51, 0x30 };

        List<byte> commands = new List<byte>();
        commands.AddRange(modelQR);
        commands.AddRange(sizeQR);
        commands.AddRange(errorQR);
        commands.AddRange(storeQR);
        commands.AddRange(Encoding.ASCII.GetBytes(qrData));
        commands.AddRange(printQR);

        return commands.ToArray();
    }
}
