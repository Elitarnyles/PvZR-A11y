using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

// Metadata-only reader for the game's IL2CPP interop assemblies.
//
// Those assemblies are stubs: every method body is a call into native code, so a
// decompiler shows nothing useful. What they do carry, in full, is the shape of the
// game - every type, field and signature. This reads that directly out of the PE
// metadata without loading anything, which means it works on assemblies that could
// never be loaded into this process.
//
// It is the single most useful tool in this repository. Nearly every question that
// came up while building the mod - what does the board expose, how is a level chosen,
// what does a zombie know about itself - was answered by pointing it at a DLL.
//
//   AsmDump <assembly.dll> <type-name-regex> [--members] [--max N]

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: AsmDump <assembly.dll> <type-regex> [--members] [--max N]");
    return 1;
}

string asmPath = args[0];
var typeRx = new Regex(args[1], RegexOptions.IgnoreCase);
bool members = args.Contains("--members");
int max = 400;
int maxIdx = Array.IndexOf(args, "--max");
if (maxIdx >= 0 && maxIdx + 1 < args.Length) max = int.Parse(args[maxIdx + 1]);

using var fs = File.OpenRead(asmPath);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var prov = new StrProvider(md);

int shown = 0;
var sb = new StringBuilder();

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

    if (!typeRx.IsMatch(full)) continue;
    if (shown++ >= max) { sb.AppendLine($"... (truncated at {max})"); break; }

    string baseName = "";
    if (!td.BaseType.IsNil) baseName = " : " + TypeRefName(md, td.BaseType);
    sb.AppendLine($"TYPE {full}{baseName}");

    if (!members) continue;

    foreach (var fh in td.GetFields())
    {
        var fd = md.GetFieldDefinition(fh);
        string ft;
        try { ft = fd.DecodeSignature(prov, null); } catch { ft = "?"; }
        string mods = (fd.Attributes & FieldAttributes.Static) != 0 ? "static " : "";
        string vis = (fd.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public ? "pub" : "prv";
        sb.AppendLine($"    F [{vis}] {mods}{ft} {md.GetString(fd.Name)}");
    }

    foreach (var mh in td.GetMethods())
    {
        var mdf = md.GetMethodDefinition(mh);
        string sig;
        try
        {
            var s = mdf.DecodeSignature(prov, null);
            sig = $"{s.ReturnType} ({string.Join(", ", s.ParameterTypes)})";
        }
        catch { sig = "?"; }
        string mods = (mdf.Attributes & MethodAttributes.Static) != 0 ? "static " : "";
        string vis = (mdf.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public ? "pub" : "prv";
        sb.AppendLine($"    M [{vis}] {mods}{md.GetString(mdf.Name)} {sig}");
    }
    sb.AppendLine();
}

Console.Out.Write(sb.ToString());
Console.Error.WriteLine($"[{shown} types matched]");
return 0;

static string TypeRefName(MetadataReader md, EntityHandle h)
{
    switch (h.Kind)
    {
        case HandleKind.TypeDefinition:
        {
            var t = md.GetTypeDefinition((TypeDefinitionHandle)h);
            var ns = md.GetString(t.Namespace);
            var n = md.GetString(t.Name);
            return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
        }
        case HandleKind.TypeReference:
        {
            var t = md.GetTypeReference((TypeReferenceHandle)h);
            var ns = md.GetString(t.Namespace);
            var n = md.GetString(t.Name);
            return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
        }
        default:
            return "?";
    }
}

sealed class StrProvider : ISignatureTypeProvider<string, object>
{
    private readonly MetadataReader _md;
    public StrProvider(MetadataReader md) => _md = md;

    public string GetPrimitiveType(PrimitiveTypeCode t) => t switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.IntPtr => "IntPtr",
        PrimitiveTypeCode.UIntPtr => "UIntPtr",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => t.ToString()
    };

    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw)
    {
        var t = r.GetTypeDefinition(h);
        var ns = r.GetString(t.Namespace);
        var n = r.GetString(t.Name);
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }

    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw)
    {
        var t = r.GetTypeReference(h);
        var ns = r.GetString(t.Namespace);
        var n = r.GetString(t.Name);
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }

    public string GetTypeFromSpecification(MetadataReader r, object g, TypeSpecificationHandle h, byte raw)
        => r.GetTypeSpecification(h).DecodeSignature(this, g);

    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', Math.Max(0, s.Rank - 1)) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetPinnedType(string e) => e;
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object g, int i) => "!!" + i;
    public string GetGenericTypeParameter(object g, int i) => "!" + i;
    public string GetModifiedType(string mod, string un, bool req) => un;
    public string GetFunctionPointerType(MethodSignature<string> si) => "fnptr";
}
