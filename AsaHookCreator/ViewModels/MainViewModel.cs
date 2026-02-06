using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using AsaHookCreator.Models;
using AsaHookCreator.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AsaHookCreator.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CppParser _parser = new();
    private readonly HookGenerator _generator = new();
    private FileSystemWatcher? _fileWatcher;
    private readonly object _watcherLock = new();
    private CancellationTokenSource? _reloadCts;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private DateTime _lastReloadTime = DateTime.MinValue;
    private static readonly HttpClient s_httpClient = new();

    /// <summary>
    /// Default raw GitHub URLs for the AsaApi header files.
    /// </summary>
    private static readonly string[] s_defaultHeaderUrls =
    [
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/Actor.h",
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/Buff.h",
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/GameMode.h",
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/Inventory.h",
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/Other.h",
        "https://raw.githubusercontent.com/ArkServerApi/AsaApi/master/AsaApi/Core/Public/API/ARK/PrimalStructure.h",
    ];

    [ObservableProperty]
    private string _headerContent = string.Empty;

    [ObservableProperty]
    private string _generatedCode = string.Empty;

    [ObservableProperty]
    private string _className = string.Empty;

    [ObservableProperty]
    private CppClass? _currentClass;

    [ObservableProperty]
    private CppFunction? _selectedFunction;

    [ObservableProperty]
    private CppField? _selectedField;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _fileFilterText = string.Empty;

    [ObservableProperty]
    private string _functionFilterText = string.Empty;

    [ObservableProperty]
    private string _fieldFilterText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentFolderPath = string.Empty;

    [ObservableProperty]
    private HeaderFileInfo? _selectedHeaderFile;

    [ObservableProperty]
    private bool _isWatching;

    // Computed properties for UI visibility
    public bool HasNoSearchResults => string.IsNullOrEmpty(SearchText) || GlobalFilteredFunctions.Count == 0;
    public bool HasNoFunctions => SelectedHeaderFile == null || Functions.Count == 0;
    public bool HasNoFunctionsLoaded => AllFunctions.Count == 0;

    public ObservableCollection<CppFunction> Functions { get; } = new();
    public ObservableCollection<CppFunction> FilteredFunctions { get; } = new();
    public ObservableCollection<CppField> Fields { get; } = new();
    public ObservableCollection<CppField> FilteredFields { get; } = new();
    public ObservableCollection<CppField> BitFields { get; } = new();
    public ObservableCollection<HeaderFileInfo> HeaderFiles { get; } = new();
    public ObservableCollection<HeaderFileInfo> FilteredHeaderFiles { get; } = new();
    
    // Global index of all functions from all files
    public ObservableCollection<CppFunction> AllFunctions { get; } = new();
    public ObservableCollection<CppFunction> GlobalFilteredFunctions { get; } = new();

    partial void OnSearchTextChanged(string value)
    {
        FilterGlobalFunctions();
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    partial void OnFileFilterTextChanged(string value)
    {
        FilterHeaderFiles();
    }

    partial void OnFunctionFilterTextChanged(string value)
    {
        FilterFunctions();
    }

    partial void OnFieldFilterTextChanged(string value)
    {
        FilterFields();
    }

    partial void OnSelectedHeaderFileChanged(HeaderFileInfo? value)
    {
        if (value != null)
        {
            _ = LoadHeaderFromFileInfoAsync(value);
        }
        OnPropertyChanged(nameof(HasNoFunctions));
    }

    partial void OnSelectedFunctionChanged(CppFunction? value)
    {
        if (value != null)
        {
            SelectedField = null;
        }
    }

    partial void OnSelectedFieldChanged(CppField? value)
    {
        if (value != null)
        {
            SelectedFunction = null;
        }
    }

    [RelayCommand]
    private async Task LoadHeaderFileAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Header files (*.h;*.hpp)|*.h;*.hpp|All files (*.*)|*.*",
            Title = "Select C++ Header File"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            IsLoading = true;
            ProgressText = "Loading...";
            StatusMessage = "Loading header file...";

            try
            {
                HeaderContent = await File.ReadAllTextAsync(openFileDialog.FileName);
                await ParseHeaderAsync();
                StatusMessage = $"Loaded: {Path.GetFileName(openFileDialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                ProgressText = string.Empty;
            }
        }
    }

    [RelayCommand]
    private async Task LoadDefaultHeadersAsync()
    {
        IsLoading = true;
        ProgressText = "Downloading...";
        StatusMessage = "Downloading default AsaApi headers from GitHub...";

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HeaderFiles.Clear();
                AllFunctions.Clear();
                GlobalFilteredFunctions.Clear();
            });

            StopFileWatcher();
            CurrentFolderPath = "GitHub: ArkServerApi/AsaApi";

            var headerFileResults = new ConcurrentBag<HeaderFileInfo>();
            var functionResults = new ConcurrentBag<CppFunction>();
            var totalFiles = s_defaultHeaderUrls.Length;
            var processedFiles = 0;

            await Task.Run(async () =>
            {
                var downloadTasks = s_defaultHeaderUrls.Select(async url =>
                {
                    var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                    var currentCount = Interlocked.Increment(ref processedFiles);

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        ProgressText = $"Downloading {currentCount}/{totalFiles}";
                        StatusMessage = $"Downloading: {fileName}";
                    });

                    var content = await s_httpClient.GetStringAsync(url);

                    var fileInfo = new HeaderFileInfo
                    {
                        FileName = fileName,
                        FullPath = url,
                        RelativePath = fileName,
                        Directory = "AsaApi/ARK"
                    };

                    try
                    {
                        var parser = new CppParser();
                        var parsedClass = parser.ParseHeader(content);

                        if (parsedClass != null)
                        {
                            fileInfo.FunctionCount = parsedClass.Functions.Count;
                            fileInfo.FieldCount = parsedClass.Fields.Count;
                            fileInfo.ClassName = parsedClass.ClassName;

                            foreach (var func in parsedClass.Functions)
                            {
                                func.SourceFile = fileName;
                                func.ClassName = parsedClass.ClassName;
                                functionResults.Add(func);
                            }
                        }
                    }
                    catch
                    {
                        fileInfo.FunctionCount = 0;
                        fileInfo.FieldCount = 0;
                    }

                    headerFileResults.Add(fileInfo);
                });

                await Task.WhenAll(downloadTasks);
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ProgressText = "Finalizing...";

                foreach (var header in headerFileResults.OrderBy(h => h.RelativePath))
                {
                    HeaderFiles.Add(header);
                }

                foreach (var func in functionResults.OrderBy(f => f.ClassName).ThenBy(f => f.FunctionName))
                {
                    AllFunctions.Add(func);
                }

                FilterHeaderFiles();
                FilterGlobalFunctions();
                OnPropertyChanged(nameof(HasNoSearchResults));
                OnPropertyChanged(nameof(HasNoFunctionsLoaded));
            });

            StatusMessage = $"Loaded {HeaderFiles.Count} default headers with {AllFunctions.Count} functions from GitHub";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Error downloading default headers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            ProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task LoadHeaderFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder containing header files",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            ProgressText = "Scanning...";
            StatusMessage = "Scanning folder for header files...";

            try
            {
                CurrentFolderPath = dialog.FolderName;
                await ScanFolderForHeadersAsync(dialog.FolderName);
                FilterHeaderFiles();
                
                // Setup file watcher for the folder
                SetupFileWatcher(dialog.FolderName);
                
                StatusMessage = $"Found {HeaderFiles.Count} header files with {AllFunctions.Count} functions (watching for changes)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error scanning folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                ProgressText = string.Empty;
            }
        }
    }

    private async Task ScanFolderForHeadersAsync(string folderPath)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            HeaderFiles.Clear();
            AllFunctions.Clear();
            GlobalFilteredFunctions.Clear();
        });

        var headerExtensions = new[] { ".h", ".hpp" };
        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => headerExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        var totalFiles = files.Count;
        var processedFiles = 0;

        // Use thread-safe collections for parallel processing
        var headerFileResults = new ConcurrentBag<HeaderFileInfo>();
        var functionResults = new ConcurrentBag<CppFunction>();

        // Determine optimal degree of parallelism
        var maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1);

        await Task.Run(() =>
        {
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, file =>
            {
                var currentCount = Interlocked.Increment(ref processedFiles);
                var relativePath = Path.GetRelativePath(folderPath, file);
                var fileName = Path.GetFileName(file);

                // Update progress on UI thread
                if (currentCount % 10 == 0 || currentCount == totalFiles)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        ProgressText = $"Processing {currentCount}/{totalFiles}";
                        StatusMessage = $"Parsing: {fileName}";
                    });
                }

                var fileInfo = new HeaderFileInfo
                {
                    FileName = fileName,
                    FullPath = file,
                    RelativePath = relativePath,
                    Directory = Path.GetDirectoryName(relativePath) ?? string.Empty
                };

                // Parse the file and extract functions
                try
                {
                    var content = File.ReadAllText(file);
                    var parser = new CppParser(); // Create new parser per thread for thread safety
                    var parsedClass = parser.ParseHeader(content);

                    if (parsedClass != null)
                    {
                        fileInfo.FunctionCount = parsedClass.Functions.Count;
                        fileInfo.FieldCount = parsedClass.Fields.Count;
                        fileInfo.ClassName = parsedClass.ClassName;

                        // Add functions to results with file reference
                        foreach (var func in parsedClass.Functions)
                        {
                            func.SourceFile = relativePath;
                            func.ClassName = parsedClass.ClassName;
                            functionResults.Add(func);
                        }
                    }
                }
                catch
                {
                    // Skip files that can't be parsed
                    fileInfo.FunctionCount = 0;
                    fileInfo.FieldCount = 0;
                }

                headerFileResults.Add(fileInfo);
            });
        });

        // Add all results to observable collections on UI thread
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressText = "Finalizing...";
            
            // Sort and add header files
            foreach (var header in headerFileResults.OrderBy(h => h.RelativePath))
            {
                HeaderFiles.Add(header);
            }

            // Sort and add all functions
            foreach (var func in functionResults.OrderBy(f => f.ClassName).ThenBy(f => f.FunctionName))
            {
                AllFunctions.Add(func);
            }

            FilterHeaderFiles();
            FilterGlobalFunctions();
            OnPropertyChanged(nameof(HasNoSearchResults));
            OnPropertyChanged(nameof(HasNoFunctionsLoaded));
        });
    }

    private void FilterGlobalFunctions()
    {
        GlobalFilteredFunctions.Clear();
        var search = SearchText.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(search))
        {
            // Show all functions when search is empty (limited to 200)
            var count = 0;
            foreach (var func in AllFunctions)
            {
                GlobalFilteredFunctions.Add(func);
                count++;
                if (count >= 200) break;
            }
            return;
        }

        // Split search into terms for multi-word search
        var searchTerms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var func in AllFunctions)
        {
            var searchableText = $"{func.FunctionName} {func.ClassName} {func.ReturnType} {func.SourceFile}".ToLowerInvariant();
            
            // All search terms must match
            var allMatch = searchTerms.All(term => searchableText.Contains(term));
            
            if (allMatch)
            {
                GlobalFilteredFunctions.Add(func);
                
                // Limit results to prevent UI slowdown
                if (GlobalFilteredFunctions.Count >= 200)
                    break;
            }
        }
        
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private void FilterHeaderFiles()
    {
        FilteredHeaderFiles.Clear();
        var search = FileFilterText.Trim().ToLowerInvariant();

        foreach (var header in HeaderFiles)
        {
            if (string.IsNullOrEmpty(search) ||
                header.FileName.ToLowerInvariant().Contains(search) ||
                header.RelativePath.ToLowerInvariant().Contains(search) ||
                (header.ClassName?.ToLowerInvariant().Contains(search) ?? false))
            {
                FilteredHeaderFiles.Add(header);
            }
        }
    }

    private void FilterFunctions()
    {
        FilteredFunctions.Clear();
        var search = FunctionFilterText.Trim().ToLowerInvariant();

        foreach (var func in Functions)
        {
            if (string.IsNullOrEmpty(search) ||
                func.FunctionName.ToLowerInvariant().Contains(search) ||
                func.ReturnType.ToLowerInvariant().Contains(search))
            {
                FilteredFunctions.Add(func);
            }
        }
    }

    private void FilterFields()
    {
        FilteredFields.Clear();
        var search = FieldFilterText.Trim().ToLowerInvariant();

        foreach (var field in Fields)
        {
            if (string.IsNullOrEmpty(search) ||
                field.FieldName.ToLowerInvariant().Contains(search) ||
                field.Type.ToLowerInvariant().Contains(search))
            {
                FilteredFields.Add(field);
            }
        }
    }

    private async Task LoadHeaderFromFileInfoAsync(HeaderFileInfo fileInfo)
    {
        IsLoading = true;
        ProgressText = "Loading...";
        StatusMessage = $"Loading {fileInfo.FileName}...";

        try
        {
            HeaderContent = await File.ReadAllTextAsync(fileInfo.FullPath);
            await ParseHeaderAsync();
            StatusMessage = $"Loaded: {fileInfo.FileName} - {Functions.Count} functions, {Fields.Count} fields";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            ProgressText = string.Empty;
            OnPropertyChanged(nameof(HasNoFunctions));
        }
    }

    [RelayCommand]
    private async Task ParseHeaderAsync()
    {
        if (string.IsNullOrWhiteSpace(HeaderContent))
        {
            StatusMessage = "No header content to parse";
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                CurrentClass = _parser.ParseHeader(HeaderContent);
            });

            if (CurrentClass != null)
            {
                ClassName = CurrentClass.ClassName;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Functions.Clear();
                    Fields.Clear();
                    BitFields.Clear();
                    FilteredFunctions.Clear();
                    FilteredFields.Clear();

                    foreach (var func in CurrentClass.Functions)
                    {
                        Functions.Add(func);
                        FilteredFunctions.Add(func);
                    }

                    foreach (var field in CurrentClass.Fields)
                    {
                        Fields.Add(field);
                        FilteredFields.Add(field);
                    }

                    foreach (var bitField in CurrentClass.BitFields)
                    {
                        BitFields.Add(bitField);
                    }
                });

                StatusMessage = $"Parsed: {Functions.Count} functions, {Fields.Count} fields";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Parse error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (!string.IsNullOrEmpty(GeneratedCode))
        {
            Clipboard.SetText(GeneratedCode);
            StatusMessage = "Code copied to clipboard!";
        }
    }

    [RelayCommand]
    private async Task SaveToFileAsync()
    {
        if (string.IsNullOrEmpty(GeneratedCode))
        {
            StatusMessage = "No code to save";
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "C++ Header files (*.h)|*.h|C++ Source files (*.cpp)|*.cpp|All files (*.*)|*.*",
            Title = "Save Generated Hook Code",
            FileName = SelectedFunction != null ? $"Hook_{SelectedFunction.FunctionName}.h" : "Hook.h"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                await File.WriteAllTextAsync(saveFileDialog.FileName, GeneratedCode);
                StatusMessage = $"Saved to: {Path.GetFileName(saveFileDialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void GenerateAllHooks()
    {
        if (CurrentClass == null || Functions.Count == 0)
        {
            StatusMessage = "No functions to generate hooks for";
            return;
        }

        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// ===========================================");
        sb.AppendLine($"// ASA API Hooks for {ClassName}");
        sb.AppendLine("// Generated by ASA Hook Creator");
        sb.AppendLine("// ===========================================");
        sb.AppendLine();
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        sb.AppendLine("#include \"AsaApi/Core/Public/API/UE/Math/Vector.h\"");
        sb.AppendLine("#include \"AsaApi/Core/Public/API/ARK/Actor.h\"");
        sb.AppendLine();

        foreach (var func in FilteredFunctions)
        {
            sb.AppendLine(_generator.GenerateAllHooks(func, true));
            sb.AppendLine();
        }

        // Generate Load function
        sb.AppendLine("// ===========================================");
        sb.AppendLine("// Load function - Initialize all hooks");
        sb.AppendLine("// ===========================================");
        sb.AppendLine("void SetupHooks()");
        sb.AppendLine("{");
        foreach (var func in FilteredFunctions)
        {
            var hookName = $"{func.ClassName}_{func.FunctionName}";
            var paramTypes = GetParameterTypes(func);
            sb.AppendLine($"    AsaApi::GetHooks().SetHook(\"{func.ClassName}.{func.FunctionName}({paramTypes})\",");
            sb.AppendLine($"        &Hook_{hookName},");
            sb.AppendLine($"        &{hookName});");
            sb.AppendLine();
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Generate Unload function
        sb.AppendLine("// ===========================================");
        sb.AppendLine("// Unload function - Remove all hooks");
        sb.AppendLine("// ===========================================");
        sb.AppendLine("void RemoveHooks()");
        sb.AppendLine("{");
        foreach (var func in FilteredFunctions)
        {
            var hookName = $"{func.ClassName}_{func.FunctionName}";
            var paramTypes = GetParameterTypes(func);
            sb.AppendLine($"    AsaApi::GetHooks().DisableHook(\"{func.ClassName}.{func.FunctionName}({paramTypes})\",");
            sb.AppendLine($"        &Hook_{hookName});");
            sb.AppendLine();
        }
        sb.AppendLine("}");

        var code = sb.ToString();
        
        // Open in new window
        var window = new GeneratedCodeWindow(code, $"All {FilteredFunctions.Count} hooks for {ClassName}");
        window.Owner = Application.Current.MainWindow;
        window.Show();

        StatusMessage = $"Generated hooks for {FilteredFunctions.Count} functions";
    }

    [RelayCommand]
    private void ClearAll()
    {
        HeaderContent = string.Empty;
        GeneratedCode = string.Empty;
        ClassName = string.Empty;
        CurrentClass = null;
        SelectedFunction = null;
        SelectedField = null;
        SelectedHeaderFile = null;
        Functions.Clear();
        FilteredFunctions.Clear();
        Fields.Clear();
        FilteredFields.Clear();
        BitFields.Clear();
        HeaderFiles.Clear();
        FilteredHeaderFiles.Clear();
        AllFunctions.Clear();
        GlobalFilteredFunctions.Clear();
        CurrentFolderPath = string.Empty;
        SearchText = string.Empty;
        FileFilterText = string.Empty;
        FunctionFilterText = string.Empty;
        FieldFilterText = string.Empty;
        ProgressText = string.Empty;
        
        // Stop file watcher
        StopFileWatcher();
        
        StatusMessage = "Cleared";
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(HasNoFunctions));
        OnPropertyChanged(nameof(HasNoFunctionsLoaded));
    }

    [RelayCommand]
    private void GenerateHookWindow(CppFunction? function)
    {
        if (function == null)
            return;

        var code = _generator.GenerateAllHooks(function, true);
        code += "\n\n" + _generator.GenerateRemoveHook(function);
        
        var window = new GeneratedCodeWindow(code, function.FunctionName);
        window.Owner = Application.Current.MainWindow;
        window.Show();

        StatusMessage = $"Generated hook for {function.FunctionName}";
    }

    private string GetParameterTypes(CppFunction function)
    {
        var types = new List<string>();
        
        // Don't include the class* type, only function parameters
        types.AddRange(function.Parameters.Select(p => p.Type));
        
        return string.Join(", ", types);
    }

    private void SetupFileWatcher(string folderPath)
    {
        // Dispose existing watcher
        StopFileWatcher();

        try
        {
            _fileWatcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Watch for .h and .hpp files
            _fileWatcher.Filter = "*.*";

            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileChanged;
            _fileWatcher.Renamed += OnFileRenamed;

            IsWatching = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to setup file watcher: {ex.Message}";
            IsWatching = false;
        }
    }

    private void StopFileWatcher()
    {
        lock (_watcherLock)
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Created -= OnFileChanged;
                _fileWatcher.Deleted -= OnFileChanged;
                _fileWatcher.Renamed -= OnFileRenamed;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
            IsWatching = false;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Only process header files
        var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (ext != ".h" && ext != ".hpp")
            return;

        DebouncedReload(e.FullPath, e.ChangeType.ToString());
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Check if either old or new name is a header file
        var oldExt = Path.GetExtension(e.OldFullPath).ToLowerInvariant();
        var newExt = Path.GetExtension(e.FullPath).ToLowerInvariant();
        
        if ((oldExt == ".h" || oldExt == ".hpp") || (newExt == ".h" || newExt == ".hpp"))
        {
            DebouncedReload(e.FullPath, "Renamed");
        }
    }

    private void DebouncedReload(string filePath, string changeType)
    {
        // Cancel any pending reload
        _reloadCts?.Cancel();
        _reloadCts = new CancellationTokenSource();
        var token = _reloadCts.Token;

        Task.Run(async () =>
        {
            try
            {
                // Debounce - wait before reloading
                await Task.Delay(_debounceDelay, token);

                if (token.IsCancellationRequested)
                    return;

                // Avoid reloading too frequently
                var now = DateTime.Now;
                if ((now - _lastReloadTime).TotalMilliseconds < 500)
                    return;

                _lastReloadTime = now;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Detected change: {Path.GetFileName(filePath)} ({changeType})";
                });

                // Reload the folder
                await ReloadFolderAsync();
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled, ignore
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Reload error: {ex.Message}";
                });
            }
        }, token);
    }

    private async Task ReloadFolderAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolderPath) || !Directory.Exists(CurrentFolderPath))
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressText = "Reloading...";
        });

        try
        {
            await ScanFolderForHeadersAsync(CurrentFolderPath);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                FilterHeaderFiles();
                ProgressText = string.Empty;
                StatusMessage = $"Reloaded: {HeaderFiles.Count} files, {AllFunctions.Count} functions";
                OnPropertyChanged(nameof(HasNoFunctionsLoaded));
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ProgressText = string.Empty;
                StatusMessage = $"Reload failed: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private void ToggleWatching()
    {
        if (IsWatching)
        {
            StopFileWatcher();
            StatusMessage = "File watching stopped";
        }
        else if (!string.IsNullOrEmpty(CurrentFolderPath))
        {
            SetupFileWatcher(CurrentFolderPath);
            StatusMessage = "File watching started";
        }
        else
        {
            StatusMessage = "No folder selected to watch";
        }
    }

    public void Dispose()
    {
        StopFileWatcher();
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
    }
}

