using System.Text.RegularExpressions;

namespace AsaHookCreator.Services;

using Models;

public class CppParser
{
    public CppClass ParseHeader(string headerContent)
    {
        var result = new CppClass();
        
        // Extract class name
        var classMatch = Regex.Match(headerContent, @"struct\s+(\w+)");
        if (classMatch.Success)
        {
            result.ClassName = classMatch.Groups[1].Value;
        }

        // Parse Fields
        ParseFields(headerContent, result);
        
        // Parse BitFields
        ParseBitFields(headerContent, result);
        
        // Parse Functions
        ParseFunctions(headerContent, result);

        return result;
    }

    private void ParseFields(string content, CppClass cppClass)
    {
        // Pattern for field accessors like: TObjectPtr<USkeletalMeshComponent>& MeshField() { return *GetNativePointerField<...
        var fieldPattern = @"(\S+(?:<[^>]+>)?)\s*&?\s+(\w+Field)\(\)\s*\{\s*return\s+\*GetNativePointerField<([^>]+)>";
        var matches = Regex.Matches(content, fieldPattern);

        foreach (Match match in matches)
        {
            var field = new CppField
            {
                ClassName = cppClass.ClassName,
                Type = match.Groups[1].Value.Trim(),
                FieldName = match.Groups[2].Value.Trim(),
                FullSignature = match.Value,
                IsBitField = false
            };
            cppClass.Fields.Add(field);
        }
    }

    private void ParseBitFields(string content, CppClass cppClass)
    {
        // Pattern for bitfield accessors like: BitFieldValue<bool, unsigned __int32> bIsCrouchedField() { return { this, "ACharacter.bIsCrouched" }; }
        var bitFieldPattern = @"BitFieldValue<([^,]+),\s*([^>]+)>\s+(\w+)\(\)\s*\{\s*return\s*\{\s*this,\s*""([^""]+)""\s*\}";
        var matches = Regex.Matches(content, bitFieldPattern);

        foreach (Match match in matches)
        {
            var field = new CppField
            {
                ClassName = cppClass.ClassName,
                Type = $"BitFieldValue<{match.Groups[1].Value.Trim()}, {match.Groups[2].Value.Trim()}>",
                FieldName = match.Groups[3].Value.Trim(),
                FullSignature = match.Groups[4].Value.Trim(),
                IsBitField = true
            };
            cppClass.BitFields.Add(field);
        }
    }

    private void ParseFunctions(string content, CppClass cppClass)
    {
        // Pattern for NativeCall functions
        var functionPattern = @"(?:static\s+)?(\S+(?:<[^>]+>)?)\s+(\w+)\(([^)]*)\)\s*\{\s*(?:return\s+)?NativeCall<([^>]+)>\(([^,]+),\s*""([^""]+)""";
        var matches = Regex.Matches(content, functionPattern);

        foreach (Match match in matches)
        {
            var isStatic = match.Value.TrimStart().StartsWith("static");
            var returnType = match.Groups[1].Value.Trim();
            var functionName = match.Groups[2].Value.Trim();
            var parametersStr = match.Groups[3].Value.Trim();
            var nativeCallTypes = match.Groups[4].Value.Trim();
            var thisOrNullptr = match.Groups[5].Value.Trim();
            var nativeSignature = match.Groups[6].Value.Trim();

            // Skip field accessors
            if (functionName.EndsWith("Field"))
                continue;

            var function = new CppFunction
            {
                ClassName = cppClass.ClassName,
                ReturnType = returnType,
                FunctionName = functionName,
                IsStatic = isStatic,
                NativeCallSignature = nativeSignature,
                Parameters = ParseParameters(parametersStr),
                FullSignature = match.Value
            };

            cppClass.Functions.Add(function);
        }
    }

    private List<CppParameter> ParseParameters(string parametersStr)
    {
        var parameters = new List<CppParameter>();
        
        if (string.IsNullOrWhiteSpace(parametersStr))
            return parameters;

        // Split parameters by comma, but be careful of template commas
        var paramList = SplitParameters(parametersStr);

        foreach (var param in paramList)
        {
            var trimmed = param.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Find the last space to separate type from name
            var lastSpace = FindLastTypeNameSeparator(trimmed);
            
            if (lastSpace > 0)
            {
                parameters.Add(new CppParameter
                {
                    Type = trimmed.Substring(0, lastSpace).Trim(),
                    Name = trimmed.Substring(lastSpace + 1).Trim().TrimStart('&', '*')
                });
            }
            else
            {
                parameters.Add(new CppParameter
                {
                    Type = trimmed,
                    Name = $"arg{parameters.Count}"
                });
            }
        }

        return parameters;
    }

    private List<string> SplitParameters(string parametersStr)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var c in parametersStr)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private int FindLastTypeNameSeparator(string param)
    {
        var depth = 0;
        var lastSpace = -1;

        for (int i = 0; i < param.Length; i++)
        {
            var c = param[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
            {
                lastSpace = i;
            }
        }

        return lastSpace;
    }
}

