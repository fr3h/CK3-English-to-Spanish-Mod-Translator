using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    private static SemaphoreSlim _translationSemaphore = new SemaphoreSlim(5); // Limita a 5 consultas en paralelo
    const string LocalizationFolderName = "localization";
    const string EnglishFolderName = "english";
    const string SpanishFolderName = "spanish";

    static async Task Main(string[] args)
    {
        Console.Write("Introduce la ruta de la carpeta del mod: ");
        string rootPath = Console.ReadLine();

        if (!Directory.Exists(rootPath))
        {
            Console.WriteLine("no existe ningun mod con la ruta especificada.");
            Console.ReadLine();
            return;
        }

        string localizationPath = Path.Combine(rootPath, LocalizationFolderName);

        if (!Directory.Exists(localizationPath))
        {
            Console.WriteLine("El proceso ha terminado.");
            Console.ReadLine();
            return;
        }

        string englishPath = Path.Combine(localizationPath, EnglishFolderName);

        if (!Directory.Exists(englishPath))
        {
            Console.WriteLine("No existe ninguna traducción en inglés de este mod.");
            Console.ReadLine();
            return;
        }

        string spanishPath = Path.Combine(localizationPath, SpanishFolderName);

        if (Directory.Exists(spanishPath))
        {
            if (!AskConfirmation("Ya existe una traducción al español de este mod. ¿Desea sobreescribirla? (S/N): "))
            {
                return;
            }
            Directory.Delete(spanishPath, true);
        }

        Directory.CreateDirectory(spanishPath);
        await TranslateFilesRecursivelyAsync(englishPath, spanishPath);

        Console.WriteLine("El proceso ha terminado.");
        Console.ReadLine();
    }

    static async Task TranslateFilesRecursivelyAsync(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        
        foreach (string file in Directory.GetFiles(sourcePath))
        {
            string fileName = Path.GetFileName(file);

            string newFileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName).Replace("english", "spanish");
            string newFileName = newFileNameWithoutExtension + ".yml";
            string targetFile = Path.Combine(targetPath, newFileName);

            string[] lines = File.ReadAllLines(file);

            if (lines.Length > 0)
            {
                lines[0] = lines[0].Replace("l_english:", "l_spanish:");
            }
            
            if (AskConfirmation("¿Quiere traducir el texto al Español? Este proceso puede llevar un par de minutos. (S/N): "))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    Regex regex = new Regex(@"^(\s*[\w\d_-]+)\s*:\s*""(.*?)""\s*$");
                    Match match = regex.Match(line);

                    if (match.Success)
                    {
                        string key = match.Groups[1].Value;
                        string textToTranslate = match.Groups[2].Value;

                        VariableStorage storage = new VariableStorage();
                        textToTranslate = ReplaceVariables(textToTranslate, storage);

                        Console.WriteLine($"Traduciendo: {textToTranslate}");
                        string translatedText = await TranslateWithArgosAsync(textToTranslate, "en", "es");
                        Console.WriteLine($"Traducido: {translatedText}");

                        translatedText = RestoreVariables(translatedText, storage);

                        // Reemplazar el texto original entre comillas con el texto traducido
                        lines[i] = $"{key}: \"{translatedText}\"";
                    }
                }
            }

            File.WriteAllLines(targetFile, lines, Encoding.UTF8);
        }

        foreach (string sourceSubDirPath in Directory.GetDirectories(sourcePath))
        {
            string subDirName = Path.GetFileName(sourceSubDirPath);

            string targetSubDirName = subDirName.Replace("english", "spanish");
            string targetSubDirPath = Path.Combine(targetPath, targetSubDirName);

            await TranslateFilesRecursivelyAsync(sourceSubDirPath, targetSubDirPath);
        }
    }

    static async Task<string> TranslateWithArgosAsync(string text, string fromLang, string toLang)
    {
        await _translationSemaphore.WaitAsync();

        try
        {
            string scriptPath = "translate.py";
            string args = $"\"{text}\" \"{fromLang}\" \"{toLang}\"";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"{scriptPath} {args}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using (Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, e) => tcs.SetResult(true);
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                await tcs.Task;

                return output;
            }
        }
        finally
        {
            _translationSemaphore.Release();
        }
    }


    static string ReplaceVariables(string text, VariableStorage storage)
    {
        Regex variableRegex = new Regex(@"({.*?}|\[.*?\]|\$\$.*?\$\$)");
        int counter = 0;

        return variableRegex.Replace(text, match =>
        {
            string placeholder = $"[[VAR_{counter++}]]";
            storage.Variables.Add(placeholder, match.Value);
            return placeholder;
        });
    }

    static string RestoreVariables(string text, VariableStorage storage)
    {
        foreach (var entry in storage.Variables)
        {
            text = text.Replace(entry.Key, entry.Value);
        }

        return text;
    }


    static bool AskConfirmation(string message)
    {
        while (true)
        {
            Console.Write(message);
            string response = Console.ReadLine().ToLower();
            if (response == "s" || response == "si" || response == "sí" || response == "y" || response == "yes")
            {
                return true;
            }
            else if (response == "n" || response == "no")
            {
                return false;
            }
            else
            {
                Console.WriteLine("Respuesta no válida. Por favor, introduzca 'S' o 'N'.");
            }
        }
    }
}

public class VariableStorage
{
    public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
}
