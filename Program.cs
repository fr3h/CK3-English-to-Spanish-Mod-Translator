using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;

class Program
{
    private static SemaphoreSlim _translationSemaphore = new SemaphoreSlim(3);
    const string LocalizationFolderName = "localization";
    const string EnglishFolderName = "english";
    const string SpanishFolderName = "spanish";

    private static ActionBlock<string> translationActionBlock;
    private static ConcurrentDictionary<string, string> translationCache = new ConcurrentDictionary<string, string>();
    private static readonly Regex LineRegex = new Regex(@"^(\s+)([\w\.-]+:)(\d+)?(\s+)""(.*?)""\s*$", RegexOptions.Compiled);
    private static readonly Regex VariableRegex = new Regex(@"(\[.*?\]|(\$[^$]+?\$)|(#\w+))", RegexOptions.Compiled);

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

        translationActionBlock = new ActionBlock<string>(async text =>
        {
            string translatedText = await TranslateWithArgosAsync(text, "en", "es");
            translationCache[text] = translatedText;
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 3 });

        Stopwatch timer = new Stopwatch();
        timer.Start();

        await TranslateFilesRecursivelyAsync(englishPath, spanishPath, AskConfirmation("¿Quiere traducir el texto al Español? Este proceso puede llevar un par de minutos. (S/N): "));

        translationActionBlock.Complete();
        await translationActionBlock.Completion;

        timer.Stop();
        TimeSpan elapsedTime = timer.Elapsed;

        Console.WriteLine($"El proceso de traducción ha tardado: {elapsedTime}");
        Console.ReadLine();
    }

    static async Task TranslateFilesRecursivelyAsync(string sourcePath, string targetPath, Boolean translate)
    {
        Directory.CreateDirectory(targetPath);

        var fileTasks = Directory.GetFiles(sourcePath).Select(async file =>
        {
            string fileName = Path.GetFileName(file);

            string newFileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName).Replace("english", "spanish");
            string newFileName = newFileNameWithoutExtension + ".yml";
            string targetFile = Path.Combine(targetPath, newFileName);

            string[] lines = File.ReadAllLines(file, Encoding.UTF8);

            if (lines.Length > 0)
            {
                lines[0] = lines[0].Replace("l_english:", "l_spanish:");
            }

            if (translate)
            {
                Environment.SetEnvironmentVariable("ARGOS_DEVICE_TYPE", "auto");
                await SetupArgosTranslatorAsync("en", "es");

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    Match match = LineRegex.Match(line);

                    if (match.Success)
                    {
                        string key = match.Groups[2].Value;
                        string twoPoints = match.Groups[3].Value;
                        string textToTranslate = match.Groups[5].Value;

                        VariableStorage storage = new VariableStorage();
                        textToTranslate = ReplaceVariables(textToTranslate, storage);

                        Console.WriteLine($" - file: {fileName}\n    Traduciendo: {textToTranslate}");

                        string translatedText = await TranslateWithArgosAsync(textToTranslate, "en", "es");

                        Console.WriteLine($" + file: {fileName}\n    Traducido: {translatedText}\n");

                        translatedText = RestoreVariables(translatedText, storage);

                        // Reemplazar el texto original entre comillas con el texto traducido
                        lines[i] = $" {key}{twoPoints} \"{translatedText}\"";
                    }
                }
            }

            File.WriteAllLines(targetFile, lines, Encoding.UTF8);
        });

        await Task.WhenAll(fileTasks);

        var dirTasks = Directory.GetDirectories(sourcePath).Select(async sourceSubDirPath =>
        {
            string subDirName = Path.GetFileName(sourceSubDirPath);

            string targetSubDirName = subDirName.Replace("english", "spanish");
            string targetSubDirPath = Path.Combine(targetPath, targetSubDirName);

            await TranslateFilesRecursivelyAsync(sourceSubDirPath, targetSubDirPath, translate);
        });

        await Task.WhenAll(dirTasks);
    }

    static async Task<string> TranslateWithArgosAsync(string text, string fromLang, string toLang)
    {
        if (translationCache.TryGetValue(text, out string cachedTranslation))
        {
            return cachedTranslation;
        }

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

            using (Process process = await Task.Run(() => new Process { StartInfo = startInfo, EnableRaisingEvents = true }))
            {
                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, e) => tcs.SetResult(true);
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                await tcs.Task;

                translationCache[text] = output;

                return output;
            }
        }
        finally
        {
            _translationSemaphore.Release();
        }
    }

    static async Task SetupArgosTranslatorAsync(string fromLang, string toLang)
    {
        var setupTranslationProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "setup_translation.py", fromLang, toLang }
            }
        };

        setupTranslationProcess.Start();
        setupTranslationProcess.WaitForExitAsync();
    }

    static string ReplaceVariables(string text, VariableStorage storage)
    {
        int counter = 0;

        return VariableRegex.Replace(text, match =>
        {
            string placeholder = $"[VAR_{counter++}]";
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
