using System.Text.RegularExpressions;

namespace AsaHookCreator.Models;

public class CppFunction
{
    public string ClassName { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string FullSignature { get; set; } = string.Empty;
    public List<CppParameter> Parameters { get; set; } = new();
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public string NativeCallSignature { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    
    public string DisplayName => $"{ReturnType} {FunctionName}({string.Join(", ", Parameters.Select(p => $"{p.Type} {p.Name}"))})";
    
    public override string ToString() => DisplayName;
}

public class CppParameter
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    
    public override string ToString() => $"{Type} {Name}";
}

public class CppField
{
    public string ClassName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string FullSignature { get; set; } = string.Empty;
    public bool IsBitField { get; set; }
    
    public string DisplayName => $"{Type} {FieldName}";
    
    public override string ToString() => DisplayName;
}

public class CppClass
{
    public string ClassName { get; set; } = string.Empty;
    public List<CppFunction> Functions { get; set; } = new();
    public List<CppField> Fields { get; set; } = new();
    public List<CppField> BitFields { get; set; } = new();
}

public class HeaderFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int FunctionCount { get; set; }
    public int FieldCount { get; set; }
    
    public override string ToString() => FileName;
}

