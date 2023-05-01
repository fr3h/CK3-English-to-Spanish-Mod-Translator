﻿using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    private static SemaphoreSlim _translationSemaphore = new SemaphoreSlim(Environment.ProcessorCount);
    const string LocalizationFolderName = "localization";
    const string EnglishFolderName = "english";
    const string SpanishFolderName = "spanish";
    private static int variablecounter = 0;

    private static readonly Regex LineRegex = new Regex(@"^(\s+)([\w\.-]+:)(\d+)?(\s+)""(.*?)""\s*$", RegexOptions.Compiled);
    private static readonly Regex VariableRegex = new Regex(@"(\[.*?\]|(\$[^$]+?\$)|(#\w+)|(\w+(_\w+)*_\d+)|(""))", RegexOptions.Compiled);

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

        Boolean translate = false;
        if (AskConfirmation("¿Quiere traducir el texto al Español? Este proceso puede llevar un par de minutos. (S/N): "))
        {
            Environment.SetEnvironmentVariable("ARGOS_DEVICE_TYPE", "cuda");
            await SetupArgosTranslatorAsync("en", "es");
            translate = true;
        }

        Stopwatch timer = new Stopwatch();
        timer.Start();

        await TranslateFilesRecursivelyAsync(englishPath, spanishPath, translate);

        timer.Stop();
        TimeSpan elapsedTime = timer.Elapsed;

        Console.WriteLine($"El proceso de traducción ha tardado: {elapsedTime}");
        Console.ReadLine();
    }

    static async Task TranslateFilesRecursivelyAsync(string sourcePath, string targetPath, Boolean translate)
    {
        Directory.CreateDirectory(targetPath);

        var allFilePaths = Directory.GetFiles(sourcePath, "*.yml", SearchOption.AllDirectories).ToList();

        if (translate)
        {
            int groupSize = (int)Math.Ceiling(allFilePaths.Count / (double)_translationSemaphore.CurrentCount);

            var fileGroups = new List<List<string>>();

            for (int i = 0; i < allFilePaths.Count; i += groupSize)
            {
                fileGroups.Add(allFilePaths.GetRange(i, Math.Min(groupSize, allFilePaths.Count - i)));
            }

            var groupTasks = fileGroups.Select(async group =>
            {
                await TranslateFileGroupAsync(group, sourcePath, targetPath);
            });

            await Task.WhenAll(groupTasks);
        }
        else
        {
            var fileTasks = allFilePaths.Select(async file =>
            {
                string fileName = Path.GetFileName(file);
                string newFileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName).Replace("english", "spanish");
                string newFileName = newFileNameWithoutExtension + ".yml";
                string relativePath = Path.GetRelativePath(sourcePath, file);
                string targetFile = Path.Combine(targetPath, relativePath).Replace("english", "spanish");

                string targetFileDirectory = Path.GetDirectoryName(targetFile);
                Directory.CreateDirectory(targetFileDirectory);

                string[] lines = File.ReadAllLines(file, Encoding.UTF8);

                if (lines.Length > 0)
                {
                    lines[0] = lines[0].Replace("l_english:", "l_spanish:");
                }

                File.WriteAllLines(targetFile, lines, Encoding.UTF8);
            });

            await Task.WhenAll(fileTasks);
        }
    }

    static async Task TranslateFileGroupAsync(List<string> files,string sourcePath, string targetPath)
    {
        StringBuilder sb = new StringBuilder();
        Dictionary<string, (string FileName, int LineStart, int LineEnd, VariableStorage Storage, string[] OriginalLines)> fileMarkers = new Dictionary<string, (string FileName, int LineStart, int LineEnd, VariableStorage Storage, string[] OriginalLines)>();

        int lineCounter = 0;

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);

            if (lines.Length > 0)
            {
                lines[0] = lines[0].Replace("l_english:", "l_spanish:");
            }

            int startLine = lineCounter;
            int endLine = lineCounter;

            VariableStorage variableStorage = new VariableStorage();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                Match match = LineRegex.Match(line);

                if (match.Success)
                {
                    string textToTranslate = match.Groups[5].Value;
                    string replacedText = ReplaceVariables(textToTranslate, variableStorage);
                    sb.AppendLine(replacedText);
                    endLine = lineCounter;
                    lineCounter++;
                }
            }

            string relativePath = Path.GetRelativePath(sourcePath, file);

            fileMarkers.Add(relativePath, (FileName: fileName, LineStart: startLine, LineEnd: endLine, Storage: variableStorage, OriginalLines: lines));
        }

        string allText = sb.ToString();
        string translatedText = await TranslateWithArgosAsync(allText, "en", "es");
        string[] translatedLines = translatedText.Split(Environment.NewLine);

        foreach (var entry in fileMarkers)
        {
            string relativePath = entry.Key;
            string newFileName = Path.GetFileNameWithoutExtension(entry.Value.FileName).Replace("english", "spanish") + ".yml";

            string updatedRelativePath = Path.Combine(Path.GetDirectoryName(relativePath), newFileName);
            string targetFile = Path.Combine(targetPath, updatedRelativePath);
            string targetFileDirectory = Path.GetDirectoryName(targetFile);
            Directory.CreateDirectory(targetFileDirectory);
            

            int startLine = entry.Value.LineStart;
            int endLine = entry.Value.LineEnd;
            VariableStorage variableStorage = entry.Value.Storage;
            string[] originalLines = entry.Value.OriginalLines;

            var translatedFileLines = translatedLines.Skip(startLine).Take(endLine - startLine + 1).ToArray();

            int translatedIndex = 0;

            for (int i = 0; i < originalLines.Length; i++)
            {
                string line = originalLines[i];
                Match match = LineRegex.Match(line);

                if (match.Success)
                {
                    string key = match.Groups[2].Value;
                    string twoPoints = match.Groups[3].Value;
                    string translatedLine = $" {key}{twoPoints} \"{RestoreVariables(translatedFileLines[translatedIndex], variableStorage)}\"";
                    originalLines[i] = translatedLine;
                    translatedIndex++;
                }
            }

            File.WriteAllLines(targetFile, originalLines, Encoding.UTF8);
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

            using (Process process = await Task.Run(() => new Process { StartInfo = startInfo, EnableRaisingEvents = true }))
            {
                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, e) => tcs.SetResult(true);
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string errorOutput = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(errorOutput))
                {
                    Console.WriteLine("Error en el proceso de Python:");
                    Console.WriteLine(errorOutput);
                }

                await tcs.Task;

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
        await setupTranslationProcess.WaitForExitAsync();
    }

    static string ReplaceVariables(string text, VariableStorage storage)
    {
        return VariableRegex.Replace(text, match =>
        {
            string placeholder = $"[{variablecounter++}]";
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
