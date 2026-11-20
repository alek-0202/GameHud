namespace GamesHud.Bootstrap;

public interface IBootstrapConsole
{
    bool IsInputRedirected { get; }
    string? ReadLine(string prompt);
    string ReadSecret(string prompt);
    void WriteLine(string message);
}

public sealed class SystemBootstrapConsole : IBootstrapConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine();
    }

    public string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var buffer = new List<char>();
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    var characters = buffer.ToArray();
                    try
                    {
                        return new string(characters);
                    }
                    finally
                    {
                        Array.Clear(characters);
                    }
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Count > 0) buffer.RemoveAt(buffer.Count - 1);
                    continue;
                }

                if (!char.IsControl(key.KeyChar)) buffer.Add(key.KeyChar);
            }
        }
        finally
        {
            for (var index = 0; index < buffer.Count; index++) buffer[index] = '\0';
            buffer.Clear();
        }
    }

    public void WriteLine(string message) => Console.WriteLine(message);
}
