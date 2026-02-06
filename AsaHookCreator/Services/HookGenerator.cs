using System.Text;
using AsaHookCreator.Models;

namespace AsaHookCreator.Services;

public enum HookType
{
    PreHook,
    PostHook,
    SetHook
}

public class HookGenerator
{
    public string GenerateHook(CppFunction function, HookType hookType, bool includeOriginalCall = true)
    {
        var sb = new StringBuilder();
        
        switch (hookType)
        {
            case HookType.PreHook:
                sb.AppendLine(GeneratePreHook(function, includeOriginalCall));
                break;
            case HookType.PostHook:
                sb.AppendLine(GeneratePostHook(function, includeOriginalCall));
                break;
            case HookType.SetHook:
                sb.AppendLine(GenerateSetHook(function));
                break;
        }

        return sb.ToString();
    }

    public string GenerateAllHooks(CppFunction function, bool includeDeclareHook = true)
    {
        var sb = new StringBuilder();
        
        // Generate DECLARE_HOOK macro if enabled
        if (includeDeclareHook)
        {
            sb.AppendLine(GenerateDeclareHook(function));
            sb.AppendLine();
        }
        
        // Generate hook implementation
        sb.AppendLine(GenerateHookImplementation(function));
        sb.AppendLine();
        
        // Generate SetHook call
        sb.AppendLine(GenerateSetHook(function));
        
        return sb.ToString();
    }

    public string GenerateDeclareHook(CppFunction function)
    {
        var sb = new StringBuilder();
        var paramTypes = GetParameterTypesForMacro(function);
        
        if (function.ReturnType.ToLower() == "void")
        {
            if (string.IsNullOrEmpty(paramTypes))
            {
                sb.Append($"DECLARE_HOOK({function.ClassName}_{function.FunctionName}, void, {function.ClassName}*);");
            }
            else
            {
                sb.Append($"DECLARE_HOOK({function.ClassName}_{function.FunctionName}, void, {function.ClassName}*, {paramTypes});");
            }
        }
        else
        {
            if (string.IsNullOrEmpty(paramTypes))
            {
                sb.Append($"DECLARE_HOOK({function.ClassName}_{function.FunctionName}, {function.ReturnType}, {function.ClassName}*);");
            }
            else
            {
                sb.Append($"DECLARE_HOOK({function.ClassName}_{function.FunctionName}, {function.ReturnType}, {function.ClassName}*, {paramTypes});");
            }
        }
        
        return sb.ToString();
    }

    private string GetParameterTypesForMacro(CppFunction function)
    {
        if (function.Parameters.Count == 0)
            return string.Empty;
        
        return string.Join(", ", function.Parameters.Select(p => p.Type));
    }


    private string GenerateHookImplementation(CppFunction function)
    {
        var sb = new StringBuilder();
        var paramList = GetParameterList(function, includeThis: true);
        var argList = GetArgumentList(function, includeThis: true);
        var isVoid = function.ReturnType.ToLower() == "void";
        var hookName = $"{function.ClassName}_{function.FunctionName}";
        
        sb.AppendLine($"{function.ReturnType} Hook_{hookName}({paramList})");
        sb.AppendLine("{");
        
        if (isVoid)
        {
            sb.AppendLine($"    {hookName}.original({argList});");
        }
        else
        {
            sb.AppendLine($"    return {hookName}.original({argList});");
        }
        
        sb.Append("}");
        
        return sb.ToString();
    }

    private string GeneratePreHook(CppFunction function, bool includeOriginal)
    {
        var sb = new StringBuilder();
        var paramList = GetParameterList(function, includeThis: true);
        var argList = GetArgumentList(function, includeThis: true);
        var isVoid = function.ReturnType.ToLower() == "void";
        
        sb.AppendLine($"// Pre-hook for {function.ClassName}::{function.FunctionName}");
        sb.AppendLine($"{function.ReturnType} PreHook_{function.FunctionName}({paramList})");
        sb.AppendLine("{");
        sb.AppendLine($"    // Your pre-hook logic here");
        sb.AppendLine($"    Log::GetLog()->info(\"{function.ClassName}::{function.FunctionName} - Pre-hook\");");
        
        if (includeOriginal)
        {
            sb.AppendLine();
            if (isVoid)
            {
                sb.AppendLine($"    // Call original");
                sb.AppendLine($"    {function.FunctionName}_original({argList});");
            }
            else
            {
                sb.AppendLine($"    // Call original and return result");
                sb.AppendLine($"    return {function.FunctionName}_original({argList});");
            }
        }
        
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    private string GeneratePostHook(CppFunction function, bool includeOriginal)
    {
        var sb = new StringBuilder();
        var paramList = GetParameterList(function, includeThis: true);
        var argList = GetArgumentList(function, includeThis: true);
        var isVoid = function.ReturnType.ToLower() == "void";
        
        sb.AppendLine($"// Post-hook for {function.ClassName}::{function.FunctionName}");
        sb.AppendLine($"{function.ReturnType} PostHook_{function.FunctionName}({paramList})");
        sb.AppendLine("{");
        
        if (includeOriginal)
        {
            if (isVoid)
            {
                sb.AppendLine($"    // Call original first");
                sb.AppendLine($"    {function.FunctionName}_original({argList});");
                sb.AppendLine();
                sb.AppendLine($"    // Your post-hook logic here");
                sb.AppendLine($"    Log::GetLog()->info(\"{function.ClassName}::{function.FunctionName} - Post-hook\");");
            }
            else
            {
                sb.AppendLine($"    // Call original first");
                sb.AppendLine($"    auto result = {function.FunctionName}_original({argList});");
                sb.AppendLine();
                sb.AppendLine($"    // Your post-hook logic here");
                sb.AppendLine($"    Log::GetLog()->info(\"{function.ClassName}::{function.FunctionName} - Post-hook\");");
                sb.AppendLine();
                sb.AppendLine($"    return result;");
            }
        }
        else
        {
            sb.AppendLine($"    // Your post-hook logic here");
            sb.AppendLine($"    Log::GetLog()->info(\"{function.ClassName}::{function.FunctionName} - Post-hook\");");
        }
        
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    private string GenerateSetHook(CppFunction function)
    {
        var sb = new StringBuilder();
        var hookName = $"{function.ClassName}_{function.FunctionName}";
        var paramTypes = GetParameterTypes(function, includeThis: false);
        
        sb.AppendLine($"AsaApi::GetHooks().SetHook(\"{function.ClassName}.{function.FunctionName}({paramTypes})\",");
        sb.AppendLine($"    &Hook_{hookName},");
        sb.Append($"    &{hookName});");
        
        return sb.ToString();
    }

    public string GenerateRemoveHook(CppFunction function)
    {
        var sb = new StringBuilder();
        var hookName = $"{function.ClassName}_{function.FunctionName}";
        var paramTypes = GetParameterTypes(function, includeThis: false);
        
        sb.AppendLine($"AsaApi::GetHooks().DisableHook(\"{function.ClassName}.{function.FunctionName}({paramTypes})\",");
        sb.Append($"    &Hook_{hookName});");
        
        return sb.ToString();
    }

    private string GetParameterTypes(CppFunction function, bool includeThis)
    {
        var types = new List<string>();
        
        if (!function.IsStatic && includeThis)
        {
            types.Add($"{function.ClassName}*");
        }
        
        types.AddRange(function.Parameters.Select(p => p.Type));
        
        return string.Join(", ", types);
    }

    private string GetParameterList(CppFunction function, bool includeThis)
    {
        var parameters = new List<string>();
        
        if (!function.IsStatic && includeThis)
        {
            parameters.Add($"{function.ClassName}* _this");
        }
        
        parameters.AddRange(function.Parameters.Select(p => $"{p.Type} {p.Name}"));
        
        return string.Join(", ", parameters);
    }

    private string GetArgumentList(CppFunction function, bool includeThis)
    {
        var args = new List<string>();
        
        if (!function.IsStatic && includeThis)
        {
            args.Add("_this");
        }
        
        args.AddRange(function.Parameters.Select(p => p.Name));
        
        return string.Join(", ", args);
    }

    public string GenerateFieldAccessor(CppField field)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"// Field accessor for {field.ClassName}::{field.FieldName}");
        
        if (field.IsBitField)
        {
            sb.AppendLine($"// BitField: {field.FullSignature}");
            sb.AppendLine($"auto {field.FieldName.TrimEnd('F', 'i', 'e', 'l', 'd')} = character->{field.FieldName}();");
        }
        else
        {
            sb.AppendLine($"auto& {field.FieldName.Replace("Field", "")} = instance->{field.FieldName}();");
        }
        
        return sb.ToString();
    }
}

