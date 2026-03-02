using System;
using System.Collections.Generic;
using System.Linq;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

public class ALVariable
{
    public string Name { get; set; } = string.Empty;
    public CodeTypeBase? Type
    {
        get; set;
    }
    public string Value { get; set; } = string.Empty;
    public string Pragmas { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public bool Locked
    {
        get; set;
    }

    public ALVariable(string name, CodeTypeBase type)
    {
        Name = name;
        Type = type;
    }

    public ALVariable(string name, CodeTypeBase type, string defaultValue) : this(name, type)
    {
        DefaultValue = defaultValue;
    }

    public ALVariable(string name, CodeTypeBase type, string defaultValue, string value, bool locked = false) : this(name, type, defaultValue)
    {
        Value = value;
        Locked = locked;
    }

    public ALVariable(string name, CodeTypeBase type, string defaultValue, string value, string pragmas, bool locked = false) : this(name, type, defaultValue, value, locked)
    {
        Pragmas = pragmas;
    }

    public void Write(LanguageWriter writer, ALConventionService conventions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(conventions);

        if (Type is not null && Type.Name.Equals("Label", StringComparison.OrdinalIgnoreCase))
        {
            var lockedStr = Locked ? ", Locked = true" : string.Empty;
            writer.WriteLine($"{Name}: Label '{(string.IsNullOrEmpty(Value) ? DefaultValue : Value)}'{lockedStr};");
        }
        else if (Type is not null)
        {
            var typeStr = conventions.GetTypeString(Type, null!);
            writer.WriteLine($"{Name}: {typeStr};");
        }
    }

    public bool CanBeCombined(ALVariable other)
    {
        if (other is null) return false;
        if (Type is null || other.Type is null) return false;
        if (!string.Equals(Type.Name, other.Type.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (Type.CollectionKind != other.Type.CollectionKind) return false;
        if (Type.Name.Equals("Label", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(Value, other.Value, StringComparison.Ordinal)) return false;
        }
        if (!string.Equals(Pragmas, other.Pragmas, StringComparison.Ordinal)) return false;
        return true;
    }
}

public class ALObjectProperty
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public void Write(LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine($"{Name} = {Value};");
    }
}
