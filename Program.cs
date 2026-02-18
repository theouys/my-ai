using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

//============================================================================
// This Application was written by Theo Uys.
// This allow you to run your own Deepseek AI model from command line
// It has the ability to keep your chat history and top save previous
// conversations to json files.
//============================================================================

class Program
{
    static string _how_to_act = "Always answer in English. You are a helpful assistant. If I ask for code examples add this before and after each code snipped namely, ########. Also do not explain anything I just want the code and commands. Always ensure the File paths starts with a ** and ends with a **.";
    
    // Define a message class to represent conversation entries
    class Message
    {
        public string role { get; set; } = "";
        public string content { get; set; } = "";
    }

    // Class to store loaded code files
    class CodeFile
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
        public string Language { get; set; } = "";
        public DateTime LastModified { get; set; }
    }

    static List<CodeFile> _loadedCodeContext = new List<CodeFile>();

    static async Task Main(string[] args)
    {
        var apiKey = "MYAPI";
        Console.Clear();
        
        
        ShowHelp();
        
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("API_KEY not set.");
            return;
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.deepseek.com/")
        };

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Initialize conversation history
        var conversation = new List<Message>();
        
        // Check if user wants to load a previous conversation
        //await CheckAndLoadConversation(conversation);
        
        // Ensure system prompt exists (add if not loaded)
        if (!conversation.Any(m => m.role == "system"))
        {
            conversation.Insert(0, new Message { role = "system", content = _how_to_act });
        }

        while (true)
        {
            // Get user input
            Console.Write("You: ");
            var userInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(userInput))
                continue;
                
            var lowerInput = userInput.ToLower();
            
            if (lowerInput == "exit")
            {
                // Ask if user wants to save before exiting
                Console.Write("Do you want to save the conversation before exiting? (y/n): ");
                var saveBeforeExit = Console.ReadLine()?.ToLower();
                if (saveBeforeExit == "y" || saveBeforeExit == "yes")
                {
                    await SaveConversation(conversation);
                }
                break;
            }

            if (lowerInput == "write" || lowerInput == "save")
            {
                await SaveConversation(conversation);
                continue;
            }

            if (lowerInput == "savecode")
            {
                await ExtractAndSaveCodeSnippets(conversation);
                continue;
            }

            if (lowerInput == "loadcontext")
            {
                await LoadDirectoryAsContext();
                continue;
            }

            if (lowerInput == "showcontext")
            {
                ShowLoadedContext();
                continue;
            }

            if (lowerInput == "clearcontext")
            {
                ClearLoadedContext();
                continue;
            }

            if (lowerInput == "load")
            {
                await LoadConversation(conversation);
                continue;
            }
                
            if (lowerInput == "clear")
            {
                conversation.Clear();
                conversation.Add(new Message { role = "system", content = _how_to_act });
                Console.WriteLine("Conversation history cleared.\n");
                continue;
            }

            if (lowerInput == "help")
            {
                ShowHelp();
            }

            // Build the complete context for the AI
            var messagesForAI = new List<Message>();
            
            // Add system prompt
            messagesForAI.Add(new Message { role = "system", content = _how_to_act });
            
            // Add code context if any files are loaded
            if (_loadedCodeContext.Any())
            {
                var contextMessage = BuildCodeContextMessage();
                messagesForAI.Add(new Message { role = "system", content = contextMessage });
            }
            
            // Add conversation history (excluding the original system prompt to avoid duplication)
            messagesForAI.AddRange(conversation.Where(m => m.role != "system"));
            
            // Add current user message
            messagesForAI.Add(new Message { role = "user", content = userInput });

            // Prepare the request body with full context
            var body = new
            {
                model = "deepseek-chat",
                messages = messagesForAI,
                stream = true
            };

            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.Write("AI: "); // Write prefix before streaming starts
            
            var response = await http.PostAsync("chat/completions", content);
            response.EnsureSuccessStatusCode();

            // Read the streaming response
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            var assistantResponse = new StringBuilder(); // To store the complete response

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrEmpty(line) && line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    
                    if (data == "[DONE]")
                    {
                        Console.WriteLine(); // New line after streaming completes
                        break;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                         // Check if this chunk has content
                        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                            choices.ValueKind == JsonValueKind.Array &&
                            choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];

                            // Try to get delta content (streaming)
                            if (choice.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("content", out var content_prop))
                            {
                                var content_text = content_prop.GetString();
                                if (!string.IsNullOrEmpty(content_text))
                                {
                                    Console.Write(content_text);
                                    assistantResponse.Append(content_text); // Accumulate the response
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Silently ignore parsing errors
                    }
                }
            }

            // Add user message and assistant's complete response to conversation history
            conversation.Add(new Message { role = "user", content = userInput });
            if (assistantResponse.Length > 0)
            {
                conversation.Add(new Message { role = "assistant", content = assistantResponse.ToString() });
            }

            Console.WriteLine(); // Add spacing between exchanges
        }
    }

    //============================================================================
    // ShowHelp
    //============================================================================
    static void ShowHelp()
    {
         Console.Clear();


        Console.WriteLine("        ##   ##  ##   ##   ######");
        Console.WriteLine("        ##   ##    ## ##   ###");
        Console.WriteLine("        ##   ##       ##    ###");
        Console.WriteLine("        ##   ##      ##       ###");
        Console.WriteLine("          ####      ###   #######");
        Console.WriteLine("                                      #####    ######");
        Console.WriteLine("                                     ##   ##     ##  ");
        Console.WriteLine("                                     #######     ##  ");
        Console.WriteLine("                                     ##   ##     ##  ");
        Console.WriteLine("                                     ##   ##   ######");
        
        Console.WriteLine("Type the following commands:");
        Console.WriteLine("exit        - Quit application.");
        Console.WriteLine("load        - Load a saved conversation.");
        Console.WriteLine("save        - Save a conversation.");
        Console.WriteLine("clear       - Clear loaded conversation and loaded context.");
        Console.WriteLine("savecode    - For code examples extract code to snippet folder.");
        Console.WriteLine("loadcontext - Load a directory of code files as context.");
        Console.WriteLine("showcontext - See currently loaded code files.");
        Console.WriteLine("clearcontext- Remove all loaded code files as context.");
        Console.WriteLine("================================================================");
        Console.WriteLine("");
    }

    //============================================================================
    // Load Directory as Context
    //============================================================================
    static async Task LoadDirectoryAsContext()
    {
        Console.Write("\nEnter directory path to load code files from: ");
        var directoryPath = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            Console.WriteLine("No directory specified.\n");
            return;
        }

        // Expand ~ to user directory
        if (directoryPath.StartsWith("~"))
        {
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            directoryPath = directoryPath.Replace("~", homePath);
        }

        if (!Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Directory not found: {directoryPath}\n");
            return;
        }

        Console.Write("Enter file extensions to include (comma-separated, e.g., .cs,.py,.js) or press Enter for all code files: ");
        var extensionsInput = Console.ReadLine();
        
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(extensionsInput))
        {
            foreach (var ext in extensionsInput.Split(','))
            {
                var trimmed = ext.Trim();
                if (!trimmed.StartsWith("."))
                    trimmed = "." + trimmed;
                extensions.Add(trimmed);
            }
        }

        Console.Write("Include subdirectories? (y/n): ");
        var includeSubdirs = Console.ReadLine()?.ToLower() == "y";

        try
        {
            var searchOption = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(directoryPath, "*.*", searchOption);
            
            int loadedCount = 0;
            int skippedCount = 0;
            
            _loadedCodeContext.Clear();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                
                // Skip if extensions specified and file doesn't match
                if (extensions.Any() && !extensions.Contains(ext))
                {
                    skippedCount++;
                    continue;
                }

                // Skip binary and large files
                if (IsBinaryFile(ext) || IsExcludedFile(file))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var language = GetLanguageFromExtension(ext);
                    
                    _loadedCodeContext.Add(new CodeFile
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Content = content,
                        Language = language,
                        LastModified = File.GetLastWriteTime(file)
                    });
                    
                    loadedCount++;
                    
                    // Show progress for first few files
                    if (loadedCount <= 5 || loadedCount % 50 == 0)
                    {
                        Console.Write($"\rLoaded {loadedCount} files...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError loading {file}: {ex.Message}");
                }
            }

            Console.WriteLine($"\n\nLoaded {loadedCount} code files into context. Skipped {skippedCount} files.\n");
            
            if (loadedCount > 0)
            {
                ShowLoadedContext();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading directory: {ex.Message}\n");
        }
    }

    //============================================================================
    // Build Code Context Message
    //============================================================================
    static string BuildCodeContextMessage()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("I have loaded the following code files for context:");
        sb.AppendLine();
        
        // Group by language for better organization
        var filesByLanguage = _loadedCodeContext.GroupBy(f => f.Language);
        
        foreach (var group in filesByLanguage)
        {
            sb.AppendLine($"=== {group.Key} Files ===");
            
            foreach (var file in group.OrderBy(f => f.FileName))
            {
                sb.AppendLine($"File: {file.FileName}");
                sb.AppendLine($"Path: {file.FilePath}");
                sb.AppendLine($"Last Modified: {file.LastModified:yyyy-MM-dd HH:mm}");
                sb.AppendLine("Content:");
                sb.AppendLine("```" + file.Language);
                sb.AppendLine(file.Content);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("Please use this code context to help answer my questions.");
        
        return sb.ToString();
    }

    //============================================================================
    // Show Loaded Context
    //============================================================================
    static void ShowLoadedContext()
    {
        if (!_loadedCodeContext.Any())
        {
            Console.WriteLine("No code files currently loaded in context.\n");
            return;
        }

        Console.WriteLine($"\nCurrently loaded code files ({_loadedCodeContext.Count} total):");
        Console.WriteLine(new string('-', 50));
        
        // Show summary by language
        var filesByLanguage = _loadedCodeContext.GroupBy(f => f.Language);
        
        foreach (var group in filesByLanguage)
        {
            Console.WriteLine($"\n{group.Key}:");
            foreach (var file in group.OrderBy(f => f.FileName).Take(10))
            {
                var relativePath = GetRelativePath(file.FilePath);
                Console.WriteLine($"  - {relativePath} ({file.Content.Length} chars)");
            }
            
            if (group.Count() > 10)
            {
                Console.WriteLine($"  ... and {group.Count() - 10} more files");
            }
        }
        
        Console.WriteLine("\nTotal files by language:");
        foreach (var group in filesByLanguage.OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()} files");
        }
        
        Console.WriteLine();
    }

    //============================================================================
    // Clear Loaded Context
    //============================================================================
    static void ClearLoadedContext()
    {
        _loadedCodeContext.Clear();
        Console.WriteLine("Code context cleared.\n");
    }

    //============================================================================
    // Helper: Check if file is binary
    //============================================================================
    static bool IsBinaryFile(string extension)
    {
        var binaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".so", ".dylib", ".bin", ".obj", ".o",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".svg",
            ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
            ".zip", ".tar", ".gz", ".rar", ".7z",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".pdb", ".mdf", ".ldf"
        };
        
        return binaryExtensions.Contains(extension);
    }

    //============================================================================
    // Helper: Check if file should be excluded
    //============================================================================
    static bool IsExcludedFile(string filePath)
    {
        var excludedPatterns = new[]
        {
            "node_modules",
            "bin",
            "obj",
            "Debug",
            "Release",
            ".git",
            ".vs",
            ".idea",
            "packages",
            "dist",
            "build"
        };
        
        var path = filePath.ToLower();
        return excludedPatterns.Any(pattern => path.Contains(pattern.ToLower()));
    }

    //============================================================================
    // Helper: Get language from file extension
    //============================================================================
    static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLower() switch
        {
            ".cs" => "csharp",
            ".py" => "python",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".html" => "html",
            ".htm" => "html",
            ".css" => "css",
            ".java" => "java",
            ".cpp" => "cpp",
            ".cxx" => "cpp",
            ".cc" => "cpp",
            ".c" => "c",
            ".h" => "c",
            ".hpp" => "cpp",
            ".php" => "php",
            ".rb" => "ruby",
            ".go" => "go",
            ".rs" => "rust",
            ".swift" => "swift",
            ".kt" => "kotlin",
            ".kts" => "kotlin",
            ".json" => "json",
            ".xml" => "xml",
            ".sql" => "sql",
            ".sh" => "bash",
            ".bash" => "bash",
            ".ps1" => "powershell",
            ".md" => "markdown",
            ".yml" => "yaml",
            ".yaml" => "yaml",
            _ => "text"
        };
    }

    //============================================================================
    // Helper: Get relative path for display
    //============================================================================
    static string GetRelativePath(string fullPath)
    {
        var currentDir = Directory.GetCurrentDirectory();
        if (fullPath.StartsWith(currentDir))
        {
            return fullPath.Substring(currentDir.Length).TrimStart(Path.DirectorySeparatorChar);
        }
        return fullPath;
    }

    //============================================================================
    // Save Conversation
    //============================================================================
    static async Task SaveConversation(List<Message> conversation)
    {
        if (conversation.Count <= 1) // Only system prompt exists
        {
            Console.WriteLine("No conversation to save yet.\n");
            return;
        }

        Console.Write("Enter filename to save conversation (without extension): ");
        var filename = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = $"conversation_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        // Ensure filename has .txt extension
        if (!filename.EndsWith(".txt"))
        {
            filename += ".txt";
        }

        try
        {
            // Build the conversation text
            var sb = new StringBuilder();
            sb.AppendLine($"Conversation saved on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();

            foreach (var message in conversation)
            {
                if (message.role == "system")
                {
                    sb.AppendLine($"[System] {message.content}");
                    sb.AppendLine();
                }
                else if (message.role == "user")
                {
                    sb.AppendLine($"You: {message.content}");
                }
                else if (message.role == "assistant")
                {
                    sb.AppendLine($"AI: {message.content}");
                    sb.AppendLine(); // Add blank line between exchanges
                }
            }

            // Save to file
            await File.WriteAllTextAsync(filename, sb.ToString());
            
            // Also save a JSON version for potential future loading
            var jsonFilename = filename.Replace(".txt", ".json");
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonContent = JsonSerializer.Serialize(conversation, jsonOptions);
            await File.WriteAllTextAsync(jsonFilename, jsonContent);

            Console.WriteLine($"Conversation saved to {filename} and {jsonFilename}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving conversation: {ex.Message}\n");
        }
    }

    //============================================================================
    // CheckAnd Load Conversation
    //============================================================================
    static async Task CheckAndLoadConversation(List<Message> conversation)
    {
        Console.Write("Do you want to load a previous conversation? (y/n): ");
        var response = Console.ReadLine()?.ToLower();
        
        if (response == "y" || response == "yes")
        {
            await LoadConversation(conversation);
        }
    }

    //============================================================================
    //  Load Conversation
    //============================================================================
    static async Task LoadConversation(List<Message> conversation)
    {
        Console.WriteLine("\nAvailable conversation files:");
        
        // Get all JSON files in current directory
        var jsonFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.json");
        var txtFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.txt");
        
        var allFiles = jsonFiles.Concat(txtFiles).OrderBy(f => f).ToList();
        
        if (allFiles.Count == 0)
        {
            Console.WriteLine("No conversation files found.\n");
            return;
        }

        // Display files with numbers
        for (int i = 0; i < allFiles.Count; i++)
        {
            var fileInfo = new FileInfo(allFiles[i]);
            Console.WriteLine($"{i + 1}. {fileInfo.Name} ({fileInfo.LastWriteTime:yyyy-MM-dd HH:mm})");
        }

        Console.Write("\nEnter file number to load (or 0 to cancel): ");
        if (int.TryParse(Console.ReadLine(), out int choice) && choice > 0 && choice <= allFiles.Count)
        {
            var selectedFile = allFiles[choice - 1];
            await LoadConversationFromFile(conversation, selectedFile);
        }
        else
        {
            Console.WriteLine("Load cancelled.\n");
        }
    }

    //============================================================================
    // Load Conversation from file
    //============================================================================
    static async Task LoadConversationFromFile(List<Message> conversation, string filename)
    {
        try
        {
            // Clear current conversation
            conversation.Clear();
            
            if (filename.EndsWith(".json"))
            {
                // Load from JSON file
                var jsonContent = await File.ReadAllTextAsync(filename);
                var loadedMessages = JsonSerializer.Deserialize<List<Message>>(jsonContent);
                
                if (loadedMessages != null)
                {
                    foreach (var msg in loadedMessages)
                    {
                        conversation.Add(msg);
                    }
                }
            }
            else if (filename.EndsWith(".txt"))
            {
                // Load from text file
                var lines = await File.ReadAllLinesAsync(filename);
                
                foreach (var line in lines)
                {
                    if (line.StartsWith("You: "))
                    {
                        conversation.Add(new Message { role = "user", content = line.Substring(5) });
                    }
                    else if (line.StartsWith("AI: "))
                    {
                        conversation.Add(new Message { role = "assistant", content = line.Substring(4) });
                    }
                    else if (line.StartsWith("[System] "))
                    {
                        conversation.Add(new Message { role = "system", content = line.Substring(8) });
                    }
                }
            }

            // Display loaded conversation summary
            Console.WriteLine($"\nLoaded {conversation.Count} messages from {Path.GetFileName(filename)}");
            
            // Show last few messages for context
            if (conversation.Count > 0)
            {
                Console.WriteLine("\nLast messages in conversation:");
                var startIndex = Math.Max(0, conversation.Count - 4);
                for (int i = startIndex; i < conversation.Count; i++)
                {
                    var msg = conversation[i];
                    if (msg.role == "user")
                        Console.WriteLine($"You: {msg.content}");
                    else if (msg.role == "assistant")
                        Console.WriteLine($"AI: {msg.content}");
                }
            }
            
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading conversation: {ex.Message}\n");
        }
    }

    //============================================================================
    // Extract and Save Code Snippets
    //============================================================================
    static async Task ExtractAndSaveCodeSnippets(List<Message> conversation)
    {
        Console.WriteLine("\nScanning conversation for code snippets...");
        
        // Combine all assistant messages into one string for scanning
        var allAssistantMessages = string.Join("\n\n", conversation
            .Where(m => m.role == "assistant")
            .Select(m => m.content));

        await SaveCodeSnippetsFromText(allAssistantMessages);
    }

    //============================================================================
    // Save Code Snippets From Text (My Own)
    //============================================================================
    static async Task SaveCodeSnippetsFromText(string text)
    {
        string inputFilePath = "output.txt";
        string folderPath = "CodeSnippets";

        // Write input text asynchronously
        await File.WriteAllTextAsync(inputFilePath, text);

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        StreamWriter? currentWriter = null;

        await foreach (string line in File.ReadLinesAsync(inputFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string result = line.Length >= 2 ? line.Substring(0, 2) : line;

            // New file detected (**filename)
            if (result == "**")
            {
                if (currentWriter != null)
                {
                    await currentWriter.DisposeAsync();
                }

                string tempstr = line.Trim('*')
                                    .Trim(':');
                                    //.Replace('/', '-');
                

                string newFilePath = Path.Combine(folderPath, tempstr);

                if (!Directory.Exists(Path.GetDirectoryName(newFilePath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));
                }

                Console.WriteLine("Creating File = " + tempstr);

                currentWriter = new StreamWriter(newFilePath, false);
            }
            else
            {
                if (!line.Equals("########") && currentWriter != null)
                {
                    await currentWriter.WriteLineAsync(line);
                }
            }
        }

        if (currentWriter != null)
        {
            await currentWriter.DisposeAsync();
        }
    }


    //============================================================================
    // Save Code Snippets From Text
    //============================================================================
   /* static async Task SaveCodeSnippetsFromText(string text)
    {
        // Regular expression to find code blocks with optional language and filename
        var codeBlockPattern = @"```(\w+)?(?::([^:\n]+))?\n(.*?)```";
        var matches = Regex.Matches(text, codeBlockPattern, RegexOptions.Singleline);

        if (matches.Count == 0)
        {
            Console.WriteLine("No code snippets found in the conversation.\n");
            return;
        }

        Console.WriteLine($"\nFound {matches.Count} code snippet(s).");

        int savedCount = 0;
        int skippedCount = 0;

        foreach (Match match in matches)
        {
            var language = match.Groups[1].Value;
            var filename = match.Groups[2].Value;
            var code = match.Groups[3].Value.Trim();

            code = CleanupCode(code);

            if (string.IsNullOrWhiteSpace(filename))
            {
                Console.Write($"\nEnter filename for {language} snippet (or 'skip' to skip): ");
                //filename = Console.ReadLine();
                filename = "outfile_"+ savedCount;
                
                if (string.IsNullOrWhiteSpace(filename) || filename.ToLower() == "skip")
                {
                    Console.WriteLine("  Skipped.");
                    skippedCount++;
                    continue;
                }
            }

            filename = EnsureCorrectExtension(filename, language);

            try
            {
                var snippetsDir = "CodeSnippets";
                if (!Directory.Exists(snippetsDir))
                {
                    Directory.CreateDirectory(snippetsDir);
                }

                var fullPath = Path.Combine(snippetsDir, filename);
                
                if (File.Exists(fullPath))
                {
                    Console.Write($"  File '{filename}' already exists. Overwrite? (y/n): ");
                    var overwrite = Console.ReadLine()?.ToLower();
                    if (overwrite != "y" && overwrite != "yes")
                    {
                        Console.WriteLine("  Skipped.");
                        skippedCount++;
                        continue;
                    }
                }

                var header = new StringBuilder();
                header.AppendLine($"// Code snippet saved on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine($"// Language: {language}");
                header.AppendLine();

                await File.WriteAllTextAsync(fullPath, header.ToString() + code);
                
                Console.WriteLine($"  ✓ Saved to CodeSnippets/{filename}");
                savedCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Error saving {filename}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nSummary: {savedCount} file(s) saved, {skippedCount} skipped.");
        if (savedCount > 0)
        {
            Console.WriteLine($"Files are in the 'CodeSnippets' folder.\n");
        }
    }*/

    //============================================================================
    // Cleanup Code
    //============================================================================
    static string CleanupCode(string code)
    {
        var lines = code.Split('\n');
        if (lines.Length == 0) return code;

        var minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent > 0 && minIndent < int.MaxValue)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    lines[i] = lines[i].Substring(minIndent);
                }
            }
        }

        return string.Join("\n", lines);
    }

    //============================================================================
    // Ensure Correct Extension
    //============================================================================
    static string EnsureCorrectExtension(string filename, string language)
    {
        var extension = GetFileExtension(language);
        
        if (!filename.Contains('.'))
        {
            return filename + extension;
        }
        
        var currentExt = Path.GetExtension(filename);
        if (currentExt != extension && !string.IsNullOrWhiteSpace(extension))
        {
            Console.Write($"  File extension '{currentExt}' doesn't match language '{language}'. Change to '{extension}'? (y/n): ");
            var changeExt = Console.ReadLine()?.ToLower();
            if (changeExt == "y" || changeExt == "yes")
            {
                return Path.GetFileNameWithoutExtension(filename) + extension;
            }
        }
        
        return filename;
    }

    //============================================================================
    // Get File Extension
    //============================================================================
    static string GetFileExtension(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => ".cs",
            "python" or "py" => ".py",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "html" => ".html",
            "css" => ".css",
            "java" => ".java",
            "cpp" or "c++" => ".cpp",
            "c" => ".c",
            "php" => ".php",
            "ruby" or "rb" => ".rb",
            "go" => ".go",
            "rust" or "rs" => ".rs",
            "swift" => ".swift",
            "kotlin" or "kt" => ".kt",
            "json" => ".json",
            "xml" => ".xml",
            "sql" => ".sql",
            "bash" or "shell" or "sh" => ".sh",
            "powershell" or "ps1" => ".ps1",
            "markdown" or "md" => ".md",
            _ => ".txt"
        };
    }
}
