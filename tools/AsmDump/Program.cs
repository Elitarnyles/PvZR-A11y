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
//                          [--member <member-name-regex>] [--il]
//
// --member turns the search around: instead of "show me this type", it answers "which
// type has a member called this". That question came up constantly and had no answer,
// because the type regex cannot see inside a type - the mod spent a round of testing
// guessing at Vase Breaker behaviour while GameplayActivity.IsScaryPotterLevel sat four
// lines from a method the mod was already calling.
//
// --il prints the size of each method body. Against the interop stubs every method is a
// couple of bytes, which is the point: it tells you at a glance whether a DLL you have
// been handed carries real code or only shapes.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: AsmDump <assembly.dll> <type-regex> [--members] [--max N]");
    Console.Error.WriteLine("                              [--member <member-regex>] [--il]");
    return 1;
}

string asmPath = args[0];
var typeRx = new Regex(args[1], RegexOptions.IgnoreCase);
bool members = args.Contains("--members");
int max = 400;
int maxIdx = Array.IndexOf(args, "--max");
if (maxIdx >= 0 && maxIdx + 1 < args.Length) max = int.Parse(args[maxIdx + 1]);

Regex memberRx = null;
int memberIdx = Array.IndexOf(args, "--member");
if (memberIdx >= 0 && memberIdx + 1 < args.Length)
{
    memberRx = new Regex(args[memberIdx + 1], RegexOptions.IgnoreCase);
    members = true;   // asking about a member is asking to see members
}

bool showIl = args.Contains("--il");

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

    // With --member the type only counts when something inside it matches, so the whole
    // assembly can be swept for a name without knowing where it lives.
    if (memberRx != null && !HasMatchingMember(md, td, memberRx, prov)) continue;

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
        string fname = md.GetString(fd.Name);
        if (memberRx != null && !memberRx.IsMatch(fname)) continue;

        string mods = (fd.Attributes & FieldAttributes.Static) != 0 ? "static " : "";
        string vis = (fd.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public ? "pub" : "prv";

        // An enum constant without its number is half an answer: level data stores the
        // mode as a bare integer, and matching it to a name needs the value.
        string value = "";
        if ((fd.Attributes & FieldAttributes.HasDefault) != 0)
        {
            try
            {
                var c = md.GetConstant(fd.GetDefaultValue());
                value = " = " + ConstantText(md, c);
            }
            catch { }
        }

        sb.AppendLine($"    F [{vis}] {mods}{ft} {fname}{value}");
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
        string mname = md.GetString(mdf.Name);
        if (memberRx != null && !memberRx.IsMatch(mname)) continue;

        string mods = (mdf.Attributes & MethodAttributes.Static) != 0 ? "static " : "";
        string vis = (mdf.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public ? "pub" : "prv";

        string body = "";
        if (showIl)
        {
            int size = -1;
            try
            {
                if (mdf.RelativeVirtualAddress != 0)
                    size = pe.GetMethodBody(mdf.RelativeVirtualAddress).GetILContent().Length;
            }
            catch { }
            body = size < 0 ? "  [no body]" : $"  [{size} bytes IL]";
        }

        sb.AppendLine($"    M [{vis}] {mods}{mname} {sig}{body}");
    }
    sb.AppendLine();
}

Console.Out.Write(sb.ToString());
Console.Error.WriteLine($"[{shown} types matched]");
return 0;

static bool HasMatchingMember(MetadataReader md, TypeDefinition td, Regex rx, StrProvider prov)
{
    foreach (var fh in td.GetFields())
        if (rx.IsMatch(md.GetString(md.GetFieldDefinition(fh).Name))) return true;

    foreach (var mh in td.GetMethods())
        if (rx.IsMatch(md.GetString(md.GetMethodDefinition(mh).Name))) return true;

    return false;
}

static string ConstantText(MetadataReader md, Constant c)
{
    var blob = md.GetBlobReader(c.Value);
    switch (c.TypeCode)
    {
        case ConstantTypeCode.Boolean: return blob.ReadBoolean().ToString();
        case ConstantTypeCode.SByte:   return blob.ReadSByte().ToString();
        case ConstantTypeCode.Byte:    return blob.ReadByte().ToString();
        case ConstantTypeCode.Int16:   return blob.ReadInt16().ToString();
        case ConstantTypeCode.UInt16:  return blob.ReadUInt16().ToString();
        case ConstantTypeCode.Int32:   return blob.ReadInt32().ToString();
        case ConstantTypeCode.UInt32:  return blob.ReadUInt32().ToString();
        case ConstantTypeCode.Int64:   return blob.ReadInt64().ToString();
        case ConstantTypeCode.UInt64:  return blob.ReadUInt64().ToString();
        case ConstantTypeCode.Single:  return blob.ReadSingle().ToString();
        case ConstantTypeCode.Double:  return blob.ReadDouble().ToString();
        case ConstantTypeCode.String:  return "\"" + blob.ReadUTF16(blob.Length) + "\"";
        default: return "?";
    }
}

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
