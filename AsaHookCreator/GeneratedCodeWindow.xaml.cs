using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AsaHookCreator;

public partial class GeneratedCodeWindow : FluentWindow
{
    private string _plainCode = string.Empty;

    public GeneratedCodeWindow()
    {
        InitializeComponent();
    }

    public GeneratedCodeWindow(string code, string functionName = "Hook") : this()
    {
        _plainCode = code;
        SetCodeWithHighlighting(code);
        TitleText.Text = $"Hook: {functionName}";
        Title = $"Generated Hook - {functionName}";
    }

    public string Code
    {
        get => _plainCode;
        set
        {
            _plainCode = value;
            SetCodeWithHighlighting(value);
        }
    }

    public string FunctionName
    {
        set
        {
            TitleText.Text = $"Hook: {value}";
            Title = $"Generated Hook - {value}";
        }
    }

    private void SetCodeWithHighlighting(string code)
    {
        CodeRichTextBox.Document.Blocks.Clear();
        
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 22
        };

        // Split code into lines and apply highlighting
        var lines = code.Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            HighlightLine(paragraph, line);
            
            if (i < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }
        }

        CodeRichTextBox.Document.Blocks.Add(paragraph);
    }

    private void HighlightLine(Paragraph paragraph, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            paragraph.Inlines.Add(new Run(" "));
            return;
        }

        // Keywords
        var keywords = new[] { "void", "int", "bool", "float", "double", "char", "auto", "return", "if", "else", "for", "while", "class", "struct", "const", "static", "virtual", "override", "nullptr" };
        var typeKeywords = new[] { "DECLARE_HOOK", "AsaApi", "GetHooks", "SetHook", "DisableHook" };
        
        // Colors
        var keywordColor = (Color)ColorConverter.ConvertFromString("#569CD6")!;     // Blue for keywords
        var typeColor = (Color)ColorConverter.ConvertFromString("#4EC9B0")!;         // Teal for types
        var stringColor = (Color)ColorConverter.ConvertFromString("#CE9178")!;       // Orange for strings
        var functionColor = (Color)ColorConverter.ConvertFromString("#DCDCAA")!;     // Yellow for functions
        var defaultColor = (Color)ColorConverter.ConvertFromString("#D4D4D4")!;      // Default text
        var macroColor = (Color)ColorConverter.ConvertFromString("#C586C0")!;        // Purple for macros
        var numberColor = (Color)ColorConverter.ConvertFromString("#B5CEA8")!;       // Light green for numbers

        int currentIndex = 0;
        
        while (currentIndex < line.Length)
        {
            bool matched = false;

            // Check for strings
            if (line[currentIndex] == '"')
            {
                int endQuote = line.IndexOf('"', currentIndex + 1);
                if (endQuote == -1) endQuote = line.Length - 1;
                
                var str = line.Substring(currentIndex, endQuote - currentIndex + 1);
                paragraph.Inlines.Add(new Run(str) { Foreground = new SolidColorBrush(stringColor) });
                currentIndex = endQuote + 1;
                matched = true;
            }
            // Check for DECLARE_HOOK macro
            else if (line.Substring(currentIndex).StartsWith("DECLARE_HOOK"))
            {
                paragraph.Inlines.Add(new Run("DECLARE_HOOK") { Foreground = new SolidColorBrush(macroColor), FontWeight = FontWeights.SemiBold });
                currentIndex += 12;
                matched = true;
            }
            // Check for AsaApi calls
            else if (line.Substring(currentIndex).StartsWith("AsaApi"))
            {
                paragraph.Inlines.Add(new Run("AsaApi") { Foreground = new SolidColorBrush(typeColor) });
                currentIndex += 6;
                matched = true;
            }
            // Check for Hook_ prefix
            else if (line.Substring(currentIndex).StartsWith("Hook_"))
            {
                // Find end of function name
                int end = currentIndex + 5;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;
                
                var funcName = line.Substring(currentIndex, end - currentIndex);
                paragraph.Inlines.Add(new Run(funcName) { Foreground = new SolidColorBrush(functionColor) });
                currentIndex = end;
                matched = true;
            }
            else
            {
                // Check for keywords
                foreach (var keyword in keywords)
                {
                    if (currentIndex + keyword.Length <= line.Length)
                    {
                        var substr = line.Substring(currentIndex, keyword.Length);
                        bool isWordBoundary = (currentIndex + keyword.Length >= line.Length || 
                                               !char.IsLetterOrDigit(line[currentIndex + keyword.Length])) &&
                                              (currentIndex == 0 || !char.IsLetterOrDigit(line[currentIndex - 1]));
                        
                        if (substr == keyword && isWordBoundary)
                        {
                            paragraph.Inlines.Add(new Run(keyword) { Foreground = new SolidColorBrush(keywordColor) });
                            currentIndex += keyword.Length;
                            matched = true;
                            break;
                        }
                    }
                }
            }

            if (!matched)
            {
                // Check for .original( pattern
                if (line.Substring(currentIndex).StartsWith(".original("))
                {
                    paragraph.Inlines.Add(new Run(".") { Foreground = new SolidColorBrush(defaultColor) });
                    paragraph.Inlines.Add(new Run("original") { Foreground = new SolidColorBrush(functionColor) });
                    paragraph.Inlines.Add(new Run("(") { Foreground = new SolidColorBrush(defaultColor) });
                    currentIndex += 10;
                }
                // Check for GetHooks() pattern
                else if (line.Substring(currentIndex).StartsWith("GetHooks"))
                {
                    paragraph.Inlines.Add(new Run("GetHooks") { Foreground = new SolidColorBrush(functionColor) });
                    currentIndex += 8;
                }
                // Check for SetHook pattern
                else if (line.Substring(currentIndex).StartsWith("SetHook"))
                {
                    paragraph.Inlines.Add(new Run("SetHook") { Foreground = new SolidColorBrush(functionColor) });
                    currentIndex += 7;
                }
                // Check for DisableHook pattern
                else if (line.Substring(currentIndex).StartsWith("DisableHook"))
                {
                    paragraph.Inlines.Add(new Run("DisableHook") { Foreground = new SolidColorBrush(functionColor) });
                    currentIndex += 11;
                }
                else
                {
                    // Default: add single character
                    paragraph.Inlines.Add(new Run(line[currentIndex].ToString()) { Foreground = new SolidColorBrush(defaultColor) });
                    currentIndex++;
                }
            }
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_plainCode);
            StatusText.Text = "Copied to clipboard!";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to copy: {ex.Message}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new SaveFileDialog
        {
            Filter = "C++ Header files (*.h)|*.h|C++ Source files (*.cpp)|*.cpp|All files (*.*)|*.*",
            Title = "Save Hook Code",
            FileName = "Hook.h"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(saveFileDialog.FileName, _plainCode);
                StatusText.Text = $"Saved to {Path.GetFileName(saveFileDialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to save: {ex.Message}";
                System.Windows.MessageBox.Show($"Error saving file: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

