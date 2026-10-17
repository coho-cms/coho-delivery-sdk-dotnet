using System.Text;
using CohoCms.Codegen;

using var http = new HttpClient();
var prompt = Console.IsInputRedirected ? null : new ConsolePrompt();
var context = new CliContext(Environment.CurrentDirectory, Environment.GetEnvironmentVariable, Console.Out, Console.Error, http, prompt);
return await Cli.RunAsync(args, context).ConfigureAwait(false);

/// <summary>Questions in the terminal. A secret is read key by key, so it is never echoed.</summary>
internal sealed class ConsolePrompt : IPrompt
{
    public string Ask(string question, string fallback)
    {
        Console.Write($"{question}{(fallback == "" ? "" : $" ({fallback})")}: ");
        var answer = Console.ReadLine()?.Trim() ?? "";
        return answer == "" ? fallback : answer;
    }

    public string AskSecret(string question)
    {
        Console.Write($"{question}: ");
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return secret.ToString().Trim();
    }
}
